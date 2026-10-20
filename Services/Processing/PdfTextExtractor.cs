using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace EduShelf.Services.Processing;

public sealed record ExtractedPage(int PageNumber, string Text);
public sealed record PdfExtractionResult(IReadOnlyList<ExtractedPage> Pages, string Text, int TotalPages, int ProcessedPages, bool IsTruncated);
public interface IPdfTextExtractor
{
    Task<PdfExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default);
}

/// <summary>PdfPig is synchronous. A child process enforces a real wall-clock limit even inside a damaged page.</summary>
public sealed class PdfTextExtractor(IOptions<ExtractionOptions> options, IOptions<StorageOptions> storageOptions) : IPdfTextExtractor, IResourceTextExtractor
{
    public async Task<PdfExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "edushelf-extraction-" + Guid.NewGuid().ToString("N") + ".pdf");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        Process? process = null;
        try
        {
            await using (var destination = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                var buffer = new byte[65536];
                long length = 0;
                int count;
                while ((count = await pdf.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    length += count;
                    if (length > storageOptions.Value.MaxFileSizeMb * 1024L * 1024L)
                        throw new ProcessingException("PdfTooLarge", "PDF файлы өңдеу шегінен асады.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), timeout.Token);
                }
            }
            var assembly = typeof(PdfExtractionProcess).Assembly.Location;
            var start = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(assembly);
            start.ArgumentList.Add("--extract-pdf");
            start.ArgumentList.Add(temporary);
            start.ArgumentList.Add(options.Value.MaxPages.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(options.Value.MaxCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(options.Value.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            process = Process.Start(start) ?? throw new ProcessingException("PdfWorkerUnavailable", "PDF өңдеу қызметі іске қосылмады.", true);
            // Both pipes are drained concurrently, preventing pipe-buffer deadlocks.
            // System.Text.Json may escape each Unicode character as six ASCII characters;
            // the combined text and page text appear twice in this bounded internal result.
            var output = ReadBoundedAsync(process.StandardOutput, options.Value.MaxCharacters * 14 + options.Value.MaxPages * 100 + 10000, timeout.Token);
            var errors = ReadBoundedAsync(process.StandardError, 16000, timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, errors);
            if (process.ExitCode != 0)
            {
                if (process.ExitCode == 3) throw new ProcessingException("PdfTimeout", "PDF мәтінін алу уақыты аяқталды.");
                var code = output.Result.Trim();
                throw code switch
                {
                    "PdfEncrypted" => new ProcessingException(code, "PDF құпиясөзбен қорғалған. Қорғалмаған көшірмесін жүктеңіз."),
                    "PdfNoText" => new ProcessingException(code, "Құжатта оқылатын мәтін табылмады. Скан PDF үшін OCR қажет."),
                    _ => new ProcessingException("PdfMalformed", "PDF мәтіні оқылмады. Файл зақымдалған немесе қолдау көрсетілмейді.")
                };
            }
            return JsonSerializer.Deserialize<PdfExtractionResult>(output.Result) ?? throw new ProcessingException("PdfMalformed", "PDF нәтижесі оқылмады.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProcessingException("PdfTimeout", "PDF мәтінін алу уақыты аяқталды.");
        }
        finally
        {
            if (process is not null)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                // On Windows Kill is asynchronous; wait for the child to release its file handle before deleting the spool file.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try { await process.WaitForExitAsync(cleanup.Token); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            try { File.Delete(temporary); } catch (IOException) { }
        }
    }

    async Task<string?> IResourceTextExtractor.ExtractAsync(Stream pdf, CancellationToken cancellationToken) => (await ExtractAsync(pdf, cancellationToken)).Text;

    private static async Task<string> ReadBoundedAsync(StreamReader reader, int maximum, CancellationToken token)
    {
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (result.Length + count > maximum) throw new ProcessingException("PdfOutputLimit", "PDF нәтижесінің көлемі шектен асты.");
            result.Append(buffer, 0, count);
        }
        return result.ToString();
    }
}

public static class PdfExtractionProcess
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--extract-pdf") return false;
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            if (args.Length is < 4 or > 5 || !int.TryParse(args[2], out var pages) || pages is < 1 or > 1000 ||
                !int.TryParse(args[3], out var characters) || characters is < 100 or > 2000000) throw new InvalidDataException();
            var seconds = 30;
            if (args.Length == 5 && (!int.TryParse(args[4], out seconds) || seconds is < 1 or > 120)) throw new InvalidDataException();
            // A self-deadline also ends an orphaned parser if the web process crashes before it can kill the child.
            using var deadline = new Timer(_ => Environment.Exit(3), null, TimeSpan.FromSeconds(seconds + 1), Timeout.InfiniteTimeSpan);
            using var stream = File.OpenRead(args[1]);
            Console.Write(JsonSerializer.Serialize(ExtractCore(stream, pages, characters)));
        }
        catch (ProcessingException ex) { Console.Write(ex.Code); Environment.ExitCode = 2; }
        catch { Console.Write("PdfMalformed"); Environment.ExitCode = 2; }
        return true;
    }

    public static PdfExtractionResult ExtractCore(Stream stream, int maxPages, int maxCharacters, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = PdfDocument.Open(stream);
            var pages = new List<ExtractedPage>();
            var combined = new StringBuilder();
            var truncated = document.NumberOfPages > maxPages;
            for (var number = 1; number <= Math.Min(document.NumberOfPages, maxPages); number++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(document.GetPage(number)).Normalize(NormalizationForm.FormC);
                text = new string(text.Where(c => !char.IsControl(c) || c is '\r' or '\n' or '\t').ToArray()).Trim();
                var available = maxCharacters - combined.Length - (combined.Length > 0 ? 2 : 0);
                if (available <= 0) { truncated = true; break; }
                if (text.Length > available) { text = text[..available]; truncated = true; }
                pages.Add(new(number, text));
                if (combined.Length > 0) combined.Append('\n', 2);
                combined.Append(text);
                if (combined.Length >= maxCharacters) { truncated |= number < document.NumberOfPages; break; }
            }
            if (string.IsNullOrWhiteSpace(combined.ToString()))
                throw new ProcessingException("PdfNoText", "Құжатта оқылатын мәтін табылмады.");
            return new(pages, combined.ToString(), document.NumberOfPages, pages.Count, truncated);
        }
        catch (OperationCanceledException) { throw; }
        catch (ProcessingException) { throw; }
        catch (Exception ex) when (ex.GetType().Name.Contains("Encrypt", StringComparison.OrdinalIgnoreCase) || ex.GetType().Name.Contains("Password", StringComparison.OrdinalIgnoreCase))
        { throw new ProcessingException("PdfEncrypted", "PDF құпиясөзбен қорғалған."); }
        catch { throw new ProcessingException("PdfMalformed", "PDF файлы оқылмады."); }
    }
}
