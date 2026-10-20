using EduShelf.Controllers;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.Services.Processing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduShelf.Tests;

public class ProcessingSqlServerTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task Concurrent_enqueue_requests_create_one_durable_job_and_preserve_analysis_request()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async number =>
        {
            await using var db = fixture.Open();
            await Queue(db).EnqueueAsync(data.ResourceId, number == 0);
        }));
        await using var verify = fixture.Open();
        var job = await verify.ProcessingJobs.SingleAsync(x => x.ResourceId == data.ResourceId);
        Assert.True(job.AnalyzeRequested);
        Assert.Equal(JobStatus.Pending, job.Status);
        Assert.Equal(0, job.Attempts);
        Assert.Equal(1, await verify.ProcessingJobs.CountAsync(x => x.ResourceId == data.ResourceId));
    }

    [SqlServerFact]
    public async Task Atomic_SQL_claim_gives_only_one_worker_the_job()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        await using (var db = fixture.Open()) await Queue(db).EnqueueAsync(data.ResourceId, false);
        var claims = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var db = fixture.Open();
            return await Queue(db).TryClaimAsync();
        }));
        Assert.Single(claims, x => x is not null);
        await using var verify = fixture.Open();
        Assert.Equal(1, (await verify.ProcessingJobs.SingleAsync(x => x.ResourceId == data.ResourceId)).Attempts);
    }

    [SqlServerFact]
    public async Task Expired_lease_is_recovered_after_restart_with_a_new_token_and_bounded_attempts()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        ProcessingLease oldLease;
        await using (var firstProcess = fixture.Open())
        {
            var queue = Queue(firstProcess);
            await queue.EnqueueAsync(data.ResourceId, true);
            firstProcess.ResourceTextExtractions.Add(new ResourceTextExtraction { ResourceId = data.ResourceId, Status = ExtractionStatus.Processing });
            await firstProcess.SaveChangesAsync();
            oldLease = (await queue.TryClaimAsync())!;
            await firstProcess.ProcessingJobs.Where(x => x.ResourceId == data.ResourceId).ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
        await using (var restartedProcess = fixture.Open())
        {
            var queue = Queue(restartedProcess);
            var recovered = (await queue.TryClaimAsync())!;
            Assert.Equal(data.ResourceId, recovered.ResourceId);
            Assert.NotEqual(oldLease.Token, recovered.Token);
            Assert.Equal(2, recovered.Attempt);
            await restartedProcess.ProcessingJobs.Where(x => x.ResourceId == data.ResourceId).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.Attempts, 3).SetProperty(x => x.LeaseExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
            Assert.Null(await queue.TryClaimAsync());
        }
        await using var verify = fixture.Open();
        var failed = await verify.ProcessingJobs.SingleAsync(x => x.ResourceId == data.ResourceId);
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("AttemptsExhausted", failed.LastErrorCode);
        var extraction = await verify.ResourceTextExtractions.SingleAsync(x => x.ResourceId == data.ResourceId);
        Assert.Equal(ExtractionStatus.Failed, extraction.Status);
        Assert.Equal("AttemptsExhausted", extraction.ErrorCode);
    }

    [SqlServerFact]
    public async Task Replaced_hash_prevents_old_worker_committing_results()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var queue = Queue(db);
        await queue.EnqueueAsync(data.ResourceId, true);
        var lease = (await queue.TryClaimAsync())!;
        await db.Resources.Where(x => x.Id == data.ResourceId).ExecuteUpdateAsync(update => update.SetProperty(x => x.FileHash, new string('b', 64)));
        await db.ProcessingJobs.Where(x => x.ResourceId == data.ResourceId).ExecuteUpdateAsync(update => update.SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.Status, JobStatus.Pending).SetProperty(x => x.FileHash, new string('b', 64)));
        var extraction = new StubExtractor();
        var ai = new StubAi(false);
        await Processor(db, queue, new StubStorage(), extraction, ai).ProcessAsync(lease);
        Assert.Equal(0, extraction.Calls);
        Assert.Equal(0, ai.Calls);
        db.ChangeTracker.Clear();
        Assert.Null((await db.Resources.SingleAsync(x => x.Id == data.ResourceId)).ExtractedText);
        Assert.False(await db.ResourceEmbeddings.AnyAsync(x => x.ResourceId == data.ResourceId));
    }

    [SqlServerFact]
    public async Task File_replaced_during_extraction_rejects_old_result_at_commit_time()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var queue = Queue(db);
        await queue.EnqueueAsync(data.ResourceId, false);
        var extractor = new CallbackExtractor(async () =>
        {
            await using var replacement = fixture.Open();
            await replacement.Resources.Where(x => x.Id == data.ResourceId).ExecuteUpdateAsync(update => update.SetProperty(x => x.FileHash, new string('c', 64)));
            await replacement.ProcessingJobs.Where(x => x.ResourceId == data.ResourceId).ExecuteUpdateAsync(update => update
                .SetProperty(x => x.FileHash, new string('c', 64)).SetProperty(x => x.LeaseToken, (Guid?)null).SetProperty(x => x.Status, JobStatus.Pending));
        });
        await Processor(db, queue, new StubStorage(), extractor, new StubAi(false)).ProcessNextAsync();
        db.ChangeTracker.Clear();
        Assert.Null((await db.Resources.SingleAsync(x => x.Id == data.ResourceId)).ExtractedText);
        Assert.False(await db.ResourceTextPages.AnyAsync(x => x.ResourceId == data.ResourceId));
        Assert.Equal(JobStatus.Pending, (await db.ProcessingJobs.SingleAsync(x => x.ResourceId == data.ResourceId)).Status);
    }

    [SqlServerFact]
    public async Task Search_index_failure_rolls_back_extraction_and_job_then_retry_recovers_atomically()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var queue = Queue(db);
        await queue.EnqueueAsync(data.ResourceId, false);
        var indexer = new FailOnceIndexer(new ResourceSearchIndexer(db));
        var processor = new ResourceProcessor(db, queue, new StubStorage(), new StubExtractor(), new StubAi(false),
            Options.Create(new AiOptions()), Options.Create(new ProcessingWorkerOptions()), indexer, NullLogger<ResourceProcessor>.Instance);
        await processor.ProcessNextAsync();
        db.ChangeTracker.Clear();
        Assert.Null((await db.Resources.SingleAsync(x => x.Id == data.ResourceId)).ExtractedText);
        Assert.Equal(JobStatus.Pending, (await db.ProcessingJobs.SingleAsync(x => x.ResourceId == data.ResourceId)).Status);
        await db.ProcessingJobs.Where(x => x.ResourceId == data.ResourceId).ExecuteUpdateAsync(update => update.SetProperty(x => x.NotBeforeUtc, DateTime.UtcNow.AddSeconds(-1)));
        await processor.ProcessNextAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(JobStatus.Completed, (await db.ProcessingJobs.SingleAsync(x => x.ResourceId == data.ResourceId)).Status);
        Assert.Contains("Text for processing", (await db.ResourceSearchDocuments.SingleAsync(x => x.ResourceId == data.ResourceId)).SearchText);
        Assert.Equal(AnalysisStatus.NotConfigured, (await db.ResourceAnalyses.SingleAsync(x => x.ResourceId == data.ResourceId)).Status);
    }

    [SqlServerFact]
    public async Task Worker_persists_real_PDF_pages_and_search_text_without_an_API_key()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        var path = Path.Combine(Path.GetTempPath(), "edushelf-job-test-" + Guid.NewGuid().ToString("N") + ".pdf");
        try
        {
            using (var pdf = ProcessingTests.TestPdf("Internal photosynthesis search word", "Second teaching page")) await File.WriteAllBytesAsync(path, pdf.ToArray());
            await using var db = fixture.Open();
            var queue = Queue(db);
            await queue.EnqueueAsync(data.ResourceId, true);
            var ai = new StubAi(false);
            var extractor = new PdfTextExtractor(Options.Create(new ExtractionOptions()), Options.Create(new StorageOptions()));
            Assert.True(await Processor(db, queue, new StubStorage(path), extractor, ai).ProcessNextAsync());
            db.ChangeTracker.Clear();
            var resource = await db.Resources.Include(x => x.TextExtraction).Include(x => x.Analysis).Include(x => x.ProcessingJob).SingleAsync(x => x.Id == data.ResourceId);
            Assert.Equal(ExtractionStatus.Completed, resource.TextExtraction!.Status);
            Assert.Equal(2, resource.TextExtraction.ProcessedPages);
            Assert.Equal(2, await db.ResourceTextPages.CountAsync(x => x.ResourceId == data.ResourceId));
            Assert.Contains("photosynthesis", resource.ExtractedText);
            Assert.Equal(AnalysisStatus.NotConfigured, resource.Analysis!.Status);
            Assert.Equal(JobStatus.Completed, resource.ProcessingJob!.Status);
            Assert.Equal(0, ai.Calls);
            Assert.Contains("photosynthesis", (await db.ResourceSearchDocuments.SingleAsync(x => x.ResourceId == data.ResourceId)).SearchText);
        }
        finally { File.Delete(path); }
    }

    [SqlServerFact]
    public async Task Worker_persists_verified_AI_and_vector_without_changing_manual_taxonomy()
    {
        await ResetJobs();
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var configured = Options.Create(new AiOptions { ApiKey = "mock-only", EmbeddingDimensions = 3 });
        var queue = new ProcessingQueue(db, configured, Options.Create(new ProcessingWorkerOptions()));
        await queue.EnqueueAsync(data.ResourceId, true);
        var ai = new StubAi(true, data.OtherSubjectId, data.OtherTopicId);
        await Processor(db, queue, new StubStorage(), new StubExtractor(), ai, configured).ProcessNextAsync();
        db.ChangeTracker.Clear();
        var resource = await db.Resources.Include(x => x.Analysis).Include(x => x.Embedding).SingleAsync(x => x.Id == data.ResourceId);
        Assert.Equal(data.SubjectId, resource.SubjectId);
        Assert.Null(resource.TopicId);
        Assert.Equal(AnalysisStatus.Completed, resource.Analysis!.Status);
        Assert.Equal(data.OtherSubjectId, resource.Analysis.SuggestedSubjectId);
        Assert.Equal(data.OtherTopicId, resource.Analysis.SuggestedTopicId);
        Assert.Equal(resource.FileHash, resource.Analysis.AnalyzedFileHash);
        Assert.Equal(3, resource.Embedding!.Dimensions);
        Assert.Equal(resource.FileHash, resource.Embedding.FileHash);
        Assert.StartsWith("[", resource.Analysis.Keywords);
    }

    [SqlServerFact]
    public async Task Controller_denies_other_teacher_status_and_analysis_requests()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var controller = Controller(db, ResourceAccessTests.Principal("not-owner", AppRoles.Teacher));
        Assert.IsType<NotFoundResult>(await controller.Status(data.ResourceId, default));
        Assert.IsType<NotFoundResult>(await controller.Analyze(data.ResourceId, default));
        Assert.IsType<NotFoundResult>(await controller.AcceptSuggestion(data.ResourceId, new string('a', 64), data.OtherSubjectId, data.OtherTopicId, default));
    }

    [SqlServerFact]
    public async Task Suggestion_acceptance_requires_current_hash_exact_suggested_IDs_and_explicit_confirmation()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var analysis = await db.ResourceAnalyses.SingleAsync(x => x.ResourceId == data.ResourceId);
        analysis.Status = AnalysisStatus.Completed;
        analysis.AnalyzedFileHash = new string('a', 64);
        analysis.SuggestedSubjectId = data.OtherSubjectId;
        analysis.SuggestedTopicId = data.OtherTopicId;
        await db.SaveChangesAsync();
        var controller = Controller(db, data.Teacher);
        await controller.AcceptSuggestion(data.ResourceId, new string('b', 64), data.OtherSubjectId, data.OtherTopicId, default);
        db.ChangeTracker.Clear();
        Assert.Equal(data.SubjectId, (await db.Resources.SingleAsync(x => x.Id == data.ResourceId)).SubjectId);
        await controller.AcceptSuggestion(data.ResourceId, new string('a', 64), data.SubjectId, null, default);
        db.ChangeTracker.Clear();
        Assert.Equal(data.SubjectId, (await db.Resources.SingleAsync(x => x.Id == data.ResourceId)).SubjectId);
        await controller.AcceptSuggestion(data.ResourceId, new string('a', 64), data.OtherSubjectId, data.OtherTopicId, default);
        db.ChangeTracker.Clear();
        var accepted = await db.Resources.SingleAsync(x => x.Id == data.ResourceId);
        Assert.Equal(data.OtherSubjectId, accepted.SubjectId);
        Assert.Equal(data.OtherTopicId, accepted.TopicId);
    }

    private async Task ResetJobs()
    {
        await using var db = fixture.Open();
        await db.ProcessingJobs.ExecuteDeleteAsync();
    }
    private static ProcessingQueue Queue(ApplicationDbContext db) => new(db, Options.Create(new AiOptions()), Options.Create(new ProcessingWorkerOptions()));
    private static ResourceProcessor Processor(ApplicationDbContext db, ProcessingQueue queue, IFileStorage storage, IPdfTextExtractor extractor, IOpenAiAnalysisClient ai, IOptions<AiOptions>? options = null)
        => new(db, queue, storage, extractor, ai, options ?? Options.Create(new AiOptions()), Options.Create(new ProcessingWorkerOptions()), new ResourceSearchIndexer(db), NullLogger<ResourceProcessor>.Instance);
    private static ProcessingController Controller(ApplicationDbContext db, System.Security.Claims.ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { User = principal };
        return new(db, Queue(db), Options.Create(new AiOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
            TempData = new TempDataDictionary(context, new MemoryTempData())
        };
    }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class StubStorage(string? path = null) : IFileStorage
    {
        public Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Delete(string storedName) { }
        public bool Exists(string storedName) => true;
        public string GetPhysicalPath(string storedName) => path ?? Path.Combine(AppContext.BaseDirectory, "DemoFiles", "kazakh-text-main-idea.pdf");
    }
    private sealed class StubExtractor : IPdfTextExtractor
    {
        public int Calls { get; private set; }
        public Task<PdfExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new PdfExtractionResult([new(1, "Text for processing")], "Text for processing", 1, 1, false));
        }
    }
    private sealed class CallbackExtractor(Func<Task> callback) : IPdfTextExtractor
    {
        public async Task<PdfExtractionResult> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default)
        {
            await callback();
            return new([new(1, "stale old file text")], "stale old file text", 1, 1, false);
        }
    }
    private sealed class FailOnceIndexer(IResourceSearchIndexer inner) : IResourceSearchIndexer
    {
        private bool failed;
        public Task UpdateAsync(int resourceId, CancellationToken cancellationToken = default)
        {
            if (!failed) { failed = true; throw new IOException("Mock index failure"); }
            return inner.UpdateAsync(resourceId, cancellationToken);
        }
    }
    private sealed class StubAi(bool configured, int? subjectId = null, int? topicId = null) : IOpenAiAnalysisClient
    {
        public bool IsConfigured => configured;
        public int Calls { get; private set; }
        public Task<AiAnalysisResult> AnalyzeAsync(string text, IReadOnlyList<AiSubject> subjects, IReadOnlyList<AiTopic> topics, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AiAnalysisResult("Тақырып", "Қазақша қысқаша мазмұн", "Орта", ["бір", "екі", "үш", "төрт", "бес"], subjectId, topicId, text.Length, 1, false));
        }
        public Task<EmbeddingResult> CreateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        { Calls++; return Task.FromResult(new EmbeddingResult("text-embedding-3-small", 3, [0.1f, 0.2f, 0.3f])); }
    }
}
