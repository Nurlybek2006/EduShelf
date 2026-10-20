using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using EduShelf.Data;
using EduShelf.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduShelf.Services.Processing;

public sealed class ResourceProcessor(ApplicationDbContext db, ProcessingQueue queue, IFileStorage files,
    IPdfTextExtractor extractor, IOpenAiAnalysisClient ai, IOptions<AiOptions> aiOptions,
    IOptions<ProcessingWorkerOptions> workerOptions, IResourceSearchIndexer indexer, ILogger<ResourceProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(CancellationToken cancellationToken = default)
    {
        var lease = await queue.TryClaimAsync(cancellationToken);
        if (lease is null) return false;
        await ProcessAsync(lease, cancellationToken);
        return true;
    }

    public async Task ProcessAsync(ProcessingLease lease, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(workerOptions.Value.ProcessingTimeoutSeconds));
        var token = timeout.Token;
        try
        {
            var source = await db.Resources.AsNoTracking().Include(x => x.TextExtraction).SingleOrDefaultAsync(x => x.Id == lease.ResourceId, token);
            if (source is null || source.FileHash != lease.FileHash) return;
            if (!await WithLeaseAsync(lease, async (resource, job) =>
            {
                resource.TextExtraction ??= new ResourceTextExtraction { ResourceId = resource.Id };
                if (resource.TextExtraction.ExtractedFileHash != resource.FileHash || resource.TextExtraction.Status != ExtractionStatus.Completed)
                    resource.TextExtraction.Status = ExtractionStatus.Processing;
                if (lease.AnalyzeRequested)
                {
                    resource.Analysis ??= new ResourceAnalysis { ResourceId = resource.Id };
                    resource.Analysis.Status = ai.IsConfigured ? AnalysisStatus.Processing : AnalysisStatus.NotConfigured;
                }
                await Task.CompletedTask;
            }, token)) return;

            PdfExtractionResult extraction;
            if (source.TextExtraction is { Status: ExtractionStatus.Completed } saved && saved.ExtractedFileHash == source.FileHash && !string.IsNullOrWhiteSpace(source.ExtractedText))
            {
                extraction = new([], source.ExtractedText, saved.TotalPages, saved.ProcessedPages, saved.IsTruncated);
            }
            else
            {
                if (!files.Exists(source.StoredFileName)) throw new ProcessingException("FileMissing", "PDF файлы табылмады. Файлды қайта жүктеңіз.");
                await using var stream = File.OpenRead(files.GetPhysicalPath(source.StoredFileName));
                extraction = await extractor.ExtractAsync(stream, token);
                if (!await WithLeaseAsync(lease, async (resource, job) =>
                {
                    resource.ExtractedText = extraction.Text;
                    var record = resource.TextExtraction ??= new ResourceTextExtraction { ResourceId = resource.Id };
                    record.Status = ExtractionStatus.Completed;
                    record.ExtractedFileHash = lease.FileHash;
                    record.TotalPages = extraction.TotalPages;
                    record.ProcessedPages = extraction.ProcessedPages;
                    record.CharacterCount = extraction.Text.Length;
                    record.IsTruncated = extraction.IsTruncated;
                    record.ExtractedAtUtc = DateTime.UtcNow;
                    record.ErrorCode = null;
                    record.ErrorMessage = null;
                    await db.ResourceTextPages.Where(x => x.ResourceId == lease.ResourceId).ExecuteDeleteAsync(token);
                    db.ResourceTextPages.AddRange(extraction.Pages.Select(x => new ResourceTextPage { ResourceId = lease.ResourceId, PageNumber = x.PageNumber, Text = x.Text }));
                }, token, refreshSearch: true)) return;
            }

            if (!lease.AnalyzeRequested) { await CompleteAsync(lease, false, token); return; }
            if (!ai.IsConfigured)
            {
                await WithLeaseAsync(lease, (resource, job) =>
                {
                    resource.Analysis ??= new ResourceAnalysis { ResourceId = resource.Id };
                    resource.Analysis.Status = AnalysisStatus.NotConfigured;
                    resource.Analysis.ErrorCode = "AiNotConfigured";
                    resource.Analysis.ErrorMessage = "AI қызметі бапталмаған.";
                    Finish(job);
                    return Task.CompletedTask;
                }, token, refreshSearch: true);
                return;
            }
            var subjects = await db.Subjects.AsNoTracking().OrderBy(x => x.Id).Select(x => new AiSubject(x.Id, x.Name)).ToListAsync(token);
            var topics = await db.Topics.AsNoTracking().OrderBy(x => x.Id).Select(x => new AiTopic(x.Id, x.SubjectId, x.Name)).ToListAsync(token);
            var result = await ai.AnalyzeAsync(extraction.Text, subjects, topics, token);
            EmbeddingResult? embedding = null;
            ProcessingException? embeddingError = null;
            if (aiOptions.Value.EnableEmbeddings)
            {
                try { embedding = await ai.CreateEmbeddingAsync(result.DetectedTopic + "\n" + result.Summary + "\n" + string.Join(", ", result.Keywords), token); }
                catch (ProcessingException ex) { embeddingError = ex; }
            }
            await WithLeaseAsync(lease, async (resource, job) =>
            {
                var analysis = resource.Analysis ??= new ResourceAnalysis { ResourceId = resource.Id };
                analysis.Status = AnalysisStatus.Completed;
                analysis.DetectedTopic = result.DetectedTopic;
                analysis.Summary = result.Summary;
                analysis.Difficulty = result.Difficulty;
                analysis.Keywords = JsonSerializer.Serialize(result.Keywords, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) });
                // Revalidate server-owned taxonomy after the network call; an administrator may have removed an entry.
                analysis.SuggestedSubjectId = result.SuggestedSubjectId.HasValue && await db.Subjects.AnyAsync(x => x.Id == result.SuggestedSubjectId, token) ? result.SuggestedSubjectId : null;
                analysis.SuggestedTopicId = result.SuggestedTopicId.HasValue && await db.Topics.AnyAsync(x => x.Id == result.SuggestedTopicId && x.SubjectId == analysis.SuggestedSubjectId, token) ? result.SuggestedTopicId : null;
                analysis.Provider = aiOptions.Value.Provider;
                analysis.Model = aiOptions.Value.Model;
                analysis.AnalyzedAtUtc = DateTime.UtcNow;
                analysis.AnalyzedFileHash = lease.FileHash;
                analysis.AnalyzedCharacters = result.AnalyzedCharacters;
                analysis.SourceCharacters = extraction.Text.Length;
                analysis.ProcessedPages = extraction.ProcessedPages;
                analysis.TotalPages = extraction.TotalPages;
                analysis.IsPartial = extraction.IsTruncated || result.IsTruncated;
                analysis.ErrorCode = embeddingError?.Code;
                analysis.ErrorMessage = embeddingError is null ? null : "AI талдауы дайын, бірақ мағыналық ұсыныстар уақытша қолжетімсіз. " + embeddingError.Message;
                if (embedding is not null)
                {
                    var stored = await db.ResourceEmbeddings.SingleOrDefaultAsync(x => x.ResourceId == resource.Id, token);
                    if (stored is null) { stored = new ResourceEmbedding { ResourceId = resource.Id }; db.ResourceEmbeddings.Add(stored); }
                    stored.Provider = aiOptions.Value.Provider;
                    stored.Model = embedding.Model;
                    stored.Dimensions = embedding.Dimensions;
                    stored.FileHash = lease.FileHash;
                    stored.VectorJson = JsonSerializer.Serialize(embedding.Vector);
                    stored.CreatedAtUtc = DateTime.UtcNow;
                    stored.CoveredCharacters = result.AnalyzedCharacters;
                    stored.IsPartial = analysis.IsPartial;
                }
                else await db.ResourceEmbeddings.Where(x => x.ResourceId == resource.Id).ExecuteDeleteAsync(token);
                Finish(job);
            }, token, refreshSearch: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { await FailAsync(lease, new ProcessingException("WorkerStopped", "Өңдеу қайта іске қосылғанда жалғасады.", true), CancellationToken.None, stopping: true); }
        catch (OperationCanceledException)
        { await FailAsync(lease, new ProcessingException("ProcessingTimeout", "Құжатты өңдеу уақыты аяқталды.", true), CancellationToken.None); }
        catch (ProcessingException ex) { await FailAsync(lease, ex, CancellationToken.None); }
        catch (Exception ex)
        {
            // Log only the type and IDs: exceptions can contain server paths, provider bodies or document content.
            logger.LogWarning("Resource {ResourceId} processing failed ({ErrorType}).", lease.ResourceId, ex.GetType().Name);
            await FailAsync(lease, new ProcessingException("ProcessingFailed", "Құжатты өңдеу кезінде қате болды. Кейінірек қайталаңыз.", true), CancellationToken.None);
        }
    }

    private Task<bool> CompleteAsync(ProcessingLease lease, bool analyzed, CancellationToken token) => WithLeaseAsync(lease, (resource, job) =>
    {
        if (!ai.IsConfigured && !(resource.Analysis?.Status == AnalysisStatus.Completed && resource.Analysis.AnalyzedFileHash == resource.FileHash))
        {
            resource.Analysis ??= new ResourceAnalysis { ResourceId = resource.Id };
            resource.Analysis.Status = AnalysisStatus.NotConfigured;
            resource.Analysis.ErrorCode = "AiNotConfigured";
            resource.Analysis.ErrorMessage = "AI қызметі бапталмаған.";
        }
        if (!analyzed && !lease.AnalyzeRequested && job.AnalyzeRequested)
        { job.Status = JobStatus.Pending; job.Attempts = 0; job.NotBeforeUtc = DateTime.UtcNow; job.LeaseToken = null; job.LeaseExpiresAtUtc = null; }
        else Finish(job);
        return Task.CompletedTask;
    }, token, refreshSearch: true);

    private Task<bool> FailAsync(ProcessingLease lease, ProcessingException error, CancellationToken token, bool stopping = false) => WithLeaseAsync(lease, (resource, job) =>
    {
        var retry = error.IsTransient && (stopping || job.Attempts < workerOptions.Value.MaxAttempts);
        job.Status = retry ? JobStatus.Pending : JobStatus.Failed;
        job.NotBeforeUtc = DateTime.UtcNow.AddSeconds(stopping ? 0 : Math.Min(60, job.Attempts * 5));
        if (stopping) job.Attempts = Math.Max(0, job.Attempts - 1);
        job.LastErrorCode = error.Code;
        job.LeaseToken = null;
        job.LeaseExpiresAtUtc = null;
        if (resource.TextExtraction is null || resource.TextExtraction.ExtractedFileHash != resource.FileHash || resource.TextExtraction.Status != ExtractionStatus.Completed)
        {
            var extraction = resource.TextExtraction ??= new ResourceTextExtraction { ResourceId = resource.Id };
            extraction.Status = retry ? ExtractionStatus.Pending : error.Code == "PdfNoText" ? ExtractionStatus.NoText : ExtractionStatus.Failed;
            extraction.ErrorCode = error.Code;
            extraction.ErrorMessage = error.Message;
        }
        if (job.AnalyzeRequested)
        {
            var analysis = resource.Analysis ??= new ResourceAnalysis { ResourceId = resource.Id };
            analysis.Status = retry ? AnalysisStatus.Pending : AnalysisStatus.Failed;
            analysis.ErrorCode = error.Code;
            analysis.ErrorMessage = error.Message;
        }
        return Task.CompletedTask;
    }, token);

    private static void Finish(ProcessingJob job)
    { job.Status = JobStatus.Completed; job.LeaseToken = null; job.LeaseExpiresAtUtc = null; job.LastErrorCode = null; }

    private async Task<bool> WithLeaseAsync(ProcessingLease lease, Func<Resource, ProcessingJob, Task> action, CancellationToken token, bool refreshSearch = false)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        // Lock resource before job everywhere. The token + file hash prevent a replaced file or recovered lease from accepting old results.
        var resource = await db.Resources.FromSqlInterpolated($"SELECT * FROM [Resources] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={lease.ResourceId}")
            .Include(x => x.TextExtraction).Include(x => x.Analysis).SingleOrDefaultAsync(token);
        var job = await db.ProcessingJobs.FromSqlInterpolated($"SELECT * FROM [ProcessingJobs] WITH (UPDLOCK, HOLDLOCK) WHERE [ResourceId]={lease.ResourceId}").SingleOrDefaultAsync(token);
        if (resource is null || job is null || resource.FileHash != lease.FileHash || job.FileHash != lease.FileHash ||
            job.LeaseToken != lease.Token || job.Status != JobStatus.Processing || job.LeaseExpiresAtUtc <= DateTime.UtcNow) return false;
        await action(resource, job);
        job.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(token);
        if (refreshSearch) await indexer.UpdateAsync(lease.ResourceId, token);
        await transaction.CommitAsync(token);
        return true;
    }
}
