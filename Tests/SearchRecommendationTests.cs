using System.Security.Claims;
using System.Text.Json;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.Services.Processing;
using EduShelf.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;

namespace EduShelf.Tests;

public class SearchRecommendationTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture fixture;
    public SearchRecommendationTests(SqlServerFixture fixture) => this.fixture = fixture;
    private static ClaimsPrincipal Guest => new(new ClaimsIdentity());
    private static ClaimsPrincipal Student(string id) => new(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id), new(ClaimTypes.Role, AppRoles.Student)], "test"));
    private static ResourceService ResourceService(ApplicationDbContext db) => new(db, new UnusedStorage(), Options.Create(new StorageOptions()), NullLogger<ResourceService>.Instance,
        new CatalogSearchService(db, Options.Create(new SearchOptions { Mode = "Like" }), NullLogger<CatalogSearchService>.Instance));

    [SqlServerFact]
    public async Task PDF_internal_words_and_AI_summary_are_searchable_with_title_ranking_filters_and_hidden_metadata()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var source = await db.Resources.Include(r => r.Analysis).SingleAsync(r => r.Id == data.ResourceId);
        var word = "жасушатану" + data.Suffix;
        source.ExtractedText = "Мәтін ішіндегі " + word + " сөзі. <script>alert('pdf')</script>";
        source.TextExtraction = new ResourceTextExtraction { Status = ExtractionStatus.Completed, ExtractedFileHash = source.FileHash, TotalPages = 1, ProcessedPages = 1 };
        source.Analysis ??= new ResourceAnalysis();
        source.Analysis.Status = AnalysisStatus.Completed; source.Analysis.AnalyzedFileHash = source.FileHash;
        source.Analysis.Summary = "Қорытынды " + word; source.Analysis.Keywords = "[\"" + word + "\"]"; source.Analysis.Difficulty = "Жоғары";
        var tag = new Tag { Name = "Іздеу " + data.Suffix };
        source.ResourceTags.Add(new ResourceTag { Tag = tag });
        var titleMatch = Copy(source, word + " атауда");
        var restricted = Copy(source, "Hidden " + data.Suffix); restricted.Description = word; restricted.AccessLevel = AccessLevel.Authenticated;
        var draft = Copy(source, "Draft " + data.Suffix); draft.Description = word; draft.Status = ResourceStatus.Draft;
        var archived = Copy(source, "Archived " + data.Suffix); archived.Description = word; archived.Status = ResourceStatus.Archived;
        db.AddRange(titleMatch, restricted, draft, archived);
        await db.SaveChangesAsync();
        var service = ResourceService(db);
        var result = await service.CatalogAsync(new CatalogQuery { Search = word }, Guest);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(titleMatch.Id, result.Items[0].Id);
        Assert.Contains(result.Items, r => r.Id == source.Id && r.Snippet!.Contains(word));
        Assert.DoesNotContain(result.Items, r => r.Id == restricted.Id || r.Id == draft.Id || r.Id == archived.Id);
        var authenticated = await service.CatalogAsync(new CatalogQuery { Search = word }, Student(data.StudentId));
        Assert.Equal(3, authenticated.TotalCount);
        var filtered = await service.CatalogAsync(new CatalogQuery { Search = word, SubjectId = source.SubjectId, TypeId = source.ResourceTypeId, TagId = tag.Id, Difficulty = "Жоғары" }, Guest);
        Assert.Single(filtered.Items); Assert.Equal(source.Id, filtered.Items[0].Id);
        var syntax = await service.CatalogAsync(new CatalogQuery { Search = "\" OR 1=1 -- [*%_" }, Guest);
        Assert.Empty(syntax.Items);
        source.Analysis.AnalyzedFileHash = new string('0', 64);
        source.ExtractedText = "Енді басқа мәтін";
        await db.SaveChangesAsync();
        var stale = await service.CatalogAsync(new CatalogQuery { Search = word, Difficulty = "Жоғары" }, Guest);
        Assert.Empty(stale.Items);
    }

    [SqlServerFact]
    public async Task Semantic_recommendations_require_matching_model_dimensions_current_hash_and_access()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var source = await db.Resources.SingleAsync(r => r.Id == data.ResourceId);
        source.Embedding = Vector(source, "model-a", 3, [1, 0, 0]);
        var good = Copy(source, "Good " + data.Suffix); good.Embedding = Vector(good, "model-a", 3, [0.99f, 0.01f, 0]);
        var lower = Copy(source, "Lower " + data.Suffix); lower.Embedding = Vector(lower, "model-a", 3, [0, 1, 0]);
        var otherModel = Copy(source, "OtherModel " + data.Suffix); otherModel.Embedding = Vector(otherModel, "model-b", 3, [1, 0, 0]);
        var dimensions = Copy(source, "OtherDimensions " + data.Suffix); dimensions.Embedding = Vector(dimensions, "model-a", 2, [1, 0]);
        var stale = Copy(source, "Stale " + data.Suffix); stale.Embedding = Vector(stale, "model-a", 3, [1, 0, 0]); stale.Embedding.FileHash = new string('0', 64);
        var privateResource = Copy(source, "Private " + data.Suffix); privateResource.AccessLevel = AccessLevel.Authenticated; privateResource.Embedding = Vector(privateResource, "model-a", 3, [1, 0, 0]);
        var draft = Copy(source, "Draft " + data.Suffix); draft.Status = ResourceStatus.Draft; draft.Embedding = Vector(draft, "model-a", 3, [1, 0, 0]);
        db.AddRange(good, lower, otherModel, dimensions, stale, privateResource, draft);
        await db.SaveChangesAsync();
        var ai = new AiOptions { ApiKey = "unit-test-placeholder", EmbeddingModel = "model-a", EmbeddingDimensions = 3 };
        var related = new RelatedResourcesService(db, ResourceService(db), Options.Create(new RecommendationOptions()), Options.Create(ai));
        var result = await related.GetAsync(source.Id, Guest);
        Assert.True(result.IsSemantic);
        Assert.Equal(new[] { good.Id, lower.Id }, result.Items.Select(r => r.Id));
        Assert.DoesNotContain(result.Items, r => r.Id == source.Id || r.Id == privateResource.Id || r.Id == draft.Id);
        var member = await related.GetAsync(source.Id, Student(data.StudentId));
        Assert.Contains(member.Items, r => r.Id == privateResource.Id);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => related.GetAsync(privateResource.Id, Guest));
        ai.ApiKey = "";
        var fallback = await related.GetAsync(source.Id, Guest);
        Assert.False(fallback.IsSemantic);
        Assert.Equal("Тақырып бойынша ұқсас", fallback.Heading);
        Assert.DoesNotContain(fallback.Items, r => r.Id == privateResource.Id || r.Id == draft.Id || r.Id == source.Id);
    }

    [SqlServerFact]
    public async Task SQL_fulltext_absence_uses_configured_fallback_and_disabled_fallback_is_graceful()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var search = new CatalogSearchService(db, Options.Create(new SearchOptions()), NullLogger<CatalogSearchService>.Instance);
        // Fixture installs no optional full-text index, even when the host engine has the component.
        Assert.False(await search.IsFullTextAvailableAsync());
        var query = db.Resources.AsNoTracking().VisibleTo(Guest, true);
        var result = await search.ApplyAsync(query, data.Suffix, "new");
        Assert.Contains("fallback", result.Mode);
        Assert.Contains(await result.Query.ToListAsync(), r => r.Id == data.ResourceId);
        var disabled = new CatalogSearchService(db, Options.Create(new SearchOptions { AllowLikeFallback = false }), NullLogger<CatalogSearchService>.Instance);
        Assert.Empty(await (await disabled.ApplyAsync(query, data.Suffix, "new")).Query.ToListAsync());
    }

    [Fact]
    public void Cosine_checks_dimensions_finite_values_and_zero_vectors()
    {
        Assert.Equal(1, RelatedResourcesService.CosineSimilarity([1, 2, 3], [1, 2, 3]), 8);
        Assert.Equal(0, RelatedResourcesService.CosineSimilarity([1, 0], [0, 1]), 8);
        Assert.Equal(0, RelatedResourcesService.CosineSimilarity([0, 0], [1, 1]), 8);
        Assert.Throws<ArgumentException>(() => RelatedResourcesService.CosineSimilarity([1], [1, 2]));
        Assert.Throws<ArgumentException>(() => RelatedResourcesService.CosineSimilarity([float.NaN], [1]));
    }

    [SqlServerFact]
    public async Task Replacing_an_analyzed_file_invalidates_results_requeues_analysis_and_preserves_history()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var source = await db.Resources.Include(r => r.Analysis).Include(r => r.ResourceTags).SingleAsync(r => r.Id == data.ResourceId);
        source.ExtractedText = "Ескі PDF мәтіні";
        source.Analysis!.Status = AnalysisStatus.Completed; source.Analysis.AnalyzedFileHash = source.FileHash; source.Analysis.Summary = "Ескі AI мазмұны";
        source.TextExtraction = new ResourceTextExtraction { ExtractedFileHash = source.FileHash, Status = ExtractionStatus.Completed,
            TotalPages = 1, ProcessedPages = 1, Pages = [new ResourceTextPage { PageNumber = 1, Text = source.ExtractedText }] };
        source.Embedding = Vector(source, "model-a", 3, [1, 0, 0]);
        source.ProcessingJob = new ProcessingJob { FileHash = source.FileHash, Status = JobStatus.Completed, AnalyzeRequested = true };
        db.ResourceViews.Add(new ResourceView { ResourceId = source.Id, UserId = data.StudentId });
        db.ResourceDownloads.Add(new ResourceDownload { ResourceId = source.Id, UserId = data.StudentId });
        db.Favorites.Add(new Favorite { ResourceId = source.Id, UserId = data.StudentId });
        db.PageBookmarks.Add(new PageBookmark { ResourceId = source.Id, UserId = data.StudentId, PageNumber = 1, FileHash = source.FileHash });
        await db.SaveChangesAsync();
        var oldStoredName = source.StoredFileName;
        var storage = new ReplacementStorage();
        var service = new ResourceService(db, storage, Options.Create(new StorageOptions()), NullLogger<ResourceService>.Instance);
        await using var stream = new MemoryStream("%PDF-1.7 replacement"u8.ToArray());
        var model = new ResourceEditViewModel { Id = source.Id, RowVersion = Convert.ToBase64String(source.RowVersion), Title = source.Title,
            Description = source.Description, SubjectId = source.SubjectId, ResourceTypeId = source.ResourceTypeId,
            Status = source.Status, AccessLevel = source.AccessLevel, File = new FormFile(stream, 0, stream.Length, "File", "replacement.pdf") };
        var teacher = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, data.TeacherId), new(ClaimTypes.Role, AppRoles.Teacher)], "test"));
        await service.UpdateAsync(source, model, teacher);
        db.ChangeTracker.Clear();
        var updated = await db.Resources.Include(r => r.Analysis).Include(r => r.TextExtraction).Include(r => r.ProcessingJob).SingleAsync(r => r.Id == data.ResourceId);
        Assert.Equal(new string('b', 64), updated.FileHash);
        Assert.Null(updated.ExtractedText); Assert.Null(updated.Analysis!.Summary); Assert.Null(updated.Analysis.AnalyzedFileHash);
        Assert.Equal(ExtractionStatus.Pending, updated.TextExtraction!.Status);
        Assert.False(await db.ResourceTextPages.AnyAsync(p => p.ResourceId == source.Id));
        Assert.False(await db.ResourceEmbeddings.AnyAsync(e => e.ResourceId == source.Id));
        Assert.Equal(JobStatus.Pending, updated.ProcessingJob!.Status); Assert.True(updated.ProcessingJob.AnalyzeRequested);
        Assert.Equal(updated.FileHash, updated.ProcessingJob.FileHash); Assert.Null(updated.ProcessingJob.LeaseToken);
        Assert.Equal(oldStoredName, storage.Deleted);
        Assert.Single(await db.ResourceViews.Where(v => v.ResourceId == source.Id).ToListAsync());
        Assert.Single(await db.ResourceDownloads.Where(v => v.ResourceId == source.Id).ToListAsync());
        Assert.True(await db.Favorites.AnyAsync(f => f.ResourceId == source.Id && f.UserId == data.StudentId));
        Assert.False(await db.PageBookmarks.AnyAsync(b => b.ResourceId == source.Id && b.FileHash == updated.FileHash));
    }

    private static Resource Copy(Resource source, string title) => new()
    {
        Title = title, Description = "Қысқаша сипаттама", SubjectId = source.SubjectId, ResourceTypeId = source.ResourceTypeId,
        OwnerId = source.OwnerId, Status = ResourceStatus.Published, AccessLevel = AccessLevel.Public,
        OriginalFileName = "sample.pdf", StoredFileName = Guid.NewGuid().ToString("N") + ".pdf", FileSize = 30,
        FileHash = new string('a', 64), ContentType = "application/pdf", Extension = ".pdf"
    };
    private static ResourceEmbedding Vector(Resource resource, string model, int dimensions, float[] vector) => new()
    {
        FileHash = resource.FileHash, Model = model, Dimensions = dimensions, Provider = "OpenAI", VectorJson = JsonSerializer.Serialize(vector)
    };
    private sealed class ReplacementStorage : IFileStorage
    {
        public string? Deleted { get; private set; }
        public Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) => Task.FromResult(new StoredFile(Guid.NewGuid().ToString("N") + ".pdf", file.FileName, "application/pdf", file.Length, ".pdf", new string('b', 64)));
        public void Delete(string storedName) => Deleted = storedName;
        public bool Exists(string storedName) => true;
        public string GetPhysicalPath(string storedName) => throw new NotSupportedException();
    }
    private sealed class UnusedStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Delete(string storedName) => throw new NotSupportedException();
        public bool Exists(string storedName) => false;
        public string GetPhysicalPath(string storedName) => throw new NotSupportedException();
    }
}
