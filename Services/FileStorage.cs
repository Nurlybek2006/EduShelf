using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EduShelf.Services;

public class StorageOptions
{
    public const string Section = "Storage";
    [Range(1, 1024)] public int MaxFileSizeMb { get; set; } = 20;
    public string RootPath { get; set; } = "App_Data/uploads";
}
public record StoredFile(string StoredFileName, string OriginalFileName, string ContentType, long FileSize, string Extension, string FileHash);
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default);
    void Delete(string storedName);
    bool Exists(string storedName);
    string GetPhysicalPath(string storedName);
}
public class FileValidationException(string message) : Exception(message);

public partial class LocalFileStorage : IFileStorage
{
    private readonly string root;
    private readonly long maxBytes;
    public LocalFileStorage(IWebHostEnvironment environment, IOptions<StorageOptions> options)
    {
        root = Path.GetFullPath(options.Value.RootPath, environment.ContentRootPath);
        var webRoot = Path.GetFullPath(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"));
        if (root.Equals(webRoot, StringComparison.OrdinalIgnoreCase) || root.StartsWith(webRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage:RootPath must be outside wwwroot.");
        maxBytes = options.Value.MaxFileSizeMb * 1024L * 1024L;
    }

    public async Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        if (file.Length < 8) throw new FileValidationException("Файл бос немесе PDF файлы зақымдалған.");
        if (file.Length > maxBytes) throw new FileValidationException($"Файл көлемі {maxBytes / 1024 / 1024} MB шегінен аспауы керек.");
        var original = Path.GetFileName(file.FileName.Replace('\\', '/'));
        if (original.Length > 255 || string.IsNullOrWhiteSpace(original) || original.Any(char.IsControl) || !Path.GetExtension(original).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new FileValidationException("Тек .pdf кеңейтімі бар файлды таңдаңыз.");
        var stored = Guid.NewGuid().ToString("N") + ".pdf";
        var path = GetPhysicalPath(stored);
        Directory.CreateDirectory(root);
        try
        {
            await using var input = file.OpenReadStream();
            var signature = new byte[5];
            var signatureLength = await input.ReadAtLeastAsync(signature, 5, throwOnEndOfStream: false, cancellationToken);
            if (signatureLength != 5 || !signature.AsSpan().SequenceEqual("%PDF-"u8))
                throw new FileValidationException("Файл сигнатурасы PDF пішіміне сәйкес келмейді.");
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await output.WriteAsync(signature, cancellationToken);
            hash.AppendData(signature);
            long size = signature.Length;
            var buffer = ArrayPool<byte>.Shared.Rent(65536);
            try
            {
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
                {
                    size += read;
                    if (size > maxBytes) throw new FileValidationException($"Файл көлемі {maxBytes / 1024 / 1024} MB шегінен асады.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    hash.AppendData(buffer, 0, read);
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
            if (size != file.Length) throw new FileValidationException("Файл толық жүктелмеді. Қайта жүктеп көріңіз.");
            return new(stored, original, "application/pdf", size, ".pdf", Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            if (File.Exists(path)) File.Delete(path);
            throw;
        }
    }

    public string GetPhysicalPath(string storedName)
    {
        if (!SafeFileName().IsMatch(storedName)) throw new FileValidationException("Файл идентификаторы дұрыс емес.");
        return Path.Combine(root, storedName);
    }
    public bool Exists(string storedName) => SafeFileName().IsMatch(storedName) && File.Exists(GetPhysicalPath(storedName));
    public void Delete(string storedName)
    {
        var path = GetPhysicalPath(storedName);
        if (File.Exists(path)) File.Delete(path);
    }
    [GeneratedRegex("^[a-f0-9]{32}\\.pdf$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeFileName();
}
