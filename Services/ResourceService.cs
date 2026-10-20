using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduShelf.Services;

public class ResourceService(ApplicationDbContext db, IFileStorage storage, IOptions<StorageOptions> storageOptions, ILogger<ResourceService> logger, CatalogSearchService? searchService = null)
{
    public static IQueryable<ResourceCardViewModel> Cards(IQueryable<Resource> query, string? userId) => query.Select(r => new ResourceCardViewModel
    {
        Id = r.Id, Title = r.Title, Description = r.Description, SubjectName = r.Subject.Name,
        TypeName = r.ResourceType.Name, OwnerName = r.Owner.FullName, ViewCount = r.Views.Count,
        DownloadCount = r.Downloads.Count, FileSize = r.FileSize, Status = r.Status, AccessLevel = r.AccessLevel,
        Difficulty = r.Analysis != null && r.Analysis.Status == AnalysisStatus.Completed && r.Analysis.AnalyzedFileHash == r.FileHash ? r.Analysis.Difficulty : null
    });

    public async Task<List<ResourceCardViewModel>> CardListAsync(IQueryable<Resource> query, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var items = await Cards(query, ResourceAccess.UserId(user)).ToListAsync(ct);
        if (user.Identity?.IsAuthenticated == true)
        {
            var ids = items.Select(x => x.Id).ToArray();
            var userId = ResourceAccess.UserId(user);
            var favorites = await db.Favorites.Where(f => f.UserId == userId && ids.Contains(f.ResourceId)).Select(f => f.ResourceId).ToListAsync(ct);
            foreach (var item in items) item.IsFavorite = favorites.Contains(item.Id);
        }
        return items;
    }

    public async Task<CatalogViewModel> CatalogAsync(CatalogQuery query, ClaimsPrincipal user, CancellationToken ct = default)
    {
        query.Page = Math.Clamp(query.Page, 1, 100000);
        query.Search = query.Search?.Trim();
        if (query.Search?.Length > 200) query.Search = query.Search[..200];
        if (query.Sort is not ("new" or "popular" or "title")) query.Sort = "new";
        var resources = db.Resources.AsNoTracking().VisibleTo(user, catalogOnly: true);
        if (query.SubjectId.HasValue) resources = resources.Where(r => r.SubjectId == query.SubjectId);
        if (query.TopicId.HasValue) resources = resources.Where(r => r.TopicId == query.TopicId);
        if (query.TypeId.HasValue) resources = resources.Where(r => r.ResourceTypeId == query.TypeId);
        if (query.TagId.HasValue) resources = resources.Where(r => r.ResourceTags.Any(t => t.TagId == query.TagId));
        if (query.Difficulty is not ("Бастапқы" or "Орта" or "Жоғары")) query.Difficulty = null;
        if (query.Difficulty is not null) resources = resources.Where(r => r.Analysis != null && r.Analysis.Status == AnalysisStatus.Completed && r.Analysis.AnalyzedFileHash == r.FileHash && r.Analysis.Difficulty == query.Difficulty);
        var search = searchService ?? new CatalogSearchService(db, Options.Create(new SearchOptions()), Microsoft.Extensions.Logging.Abstractions.NullLogger<CatalogSearchService>.Instance);
        var searched = await search.ApplyAsync(resources, query.Search, query.Sort, ct);
        resources = searched.Query;
        var count = await resources.CountAsync(ct);
        var pages = (int)Math.Ceiling(count / (double)CatalogQuery.PageSize);
        query.Page = Math.Min(query.Page, Math.Max(1, pages));
        var model = new CatalogViewModel
        {
            Query = query, TotalCount = count, PageCount = pages,
            Items = await CardListAsync(resources.Skip((query.Page - 1) * CatalogQuery.PageSize).Take(CatalogQuery.PageSize), user, ct),
            Subjects = await db.Subjects.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
            Topics = await db.Topics.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
            ResourceTypes = await db.ResourceTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
            Tags = await db.Tags.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct), SearchMode = searched.Mode
        };
        if (!string.IsNullOrEmpty(query.Search) && model.Items.Count > 0)
        {
            var ids = model.Items.Select(i => i.Id).ToArray();
            var snippets = await db.Resources.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.Description,
                Text = r.TextExtraction != null && r.TextExtraction.ExtractedFileHash == r.FileHash ? r.ExtractedText : null,
                Summary = r.Analysis != null && r.Analysis.Status == AnalysisStatus.Completed && r.Analysis.AnalyzedFileHash == r.FileHash ? r.Analysis.Summary : null }).ToListAsync(ct);
            foreach (var item in model.Items)
            {
                var source = snippets.Single(s => s.Id == item.Id);
                var match = new[] { source.Text, source.Summary, source.Description }.FirstOrDefault(s => s?.Contains(query.Search, StringComparison.OrdinalIgnoreCase) == true);
                if (match is null) continue;
                var index = match.IndexOf(query.Search, StringComparison.OrdinalIgnoreCase);
                var start = Math.Max(0, index - 70); var length = Math.Min(220, match.Length - start);
                item.Snippet = (start > 0 ? "…" : "") + match.Substring(start, length).Replace('\n', ' ') + (start + length < match.Length ? "…" : "");
            }
        }
        return model;
    }

    public async Task FillOptionsAsync(ResourceEditViewModel model, CancellationToken ct = default)
    {
        model.Subjects = await db.Subjects.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        model.Topics = await db.Topics.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        model.ResourceTypes = await db.ResourceTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        model.Tags = await db.Tags.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        model.MaxFileSizeMb = storageOptions.Value.MaxFileSizeMb;
    }

    public async Task ValidateMetadataAsync(ResourceEditViewModel model, CancellationToken ct = default)
    {
        if (!Enum.IsDefined(model.AccessLevel) || !Enum.IsDefined(model.Status)) throw new ValidationException("Материал күйі немесе қолжетімділігі дұрыс емес.");
        if (string.IsNullOrWhiteSpace(model.Title) || model.Title.Trim().Length > 200 || string.IsNullOrWhiteSpace(model.Description) || model.Description.Trim().Length > 4000)
            throw new ValidationException("Атау мен сипаттаманы дұрыс толтырыңыз.");
        if (!await db.Subjects.AnyAsync(s => s.Id == model.SubjectId, ct)) throw new ValidationException("Таңдалған пән табылмады.");
        if (model.TopicId.HasValue && !await db.Topics.AnyAsync(t => t.Id == model.TopicId && t.SubjectId == model.SubjectId, ct))
            throw new ValidationException("Тақырып таңдалған пәнге тиесілі болуы керек.");
        if (!await db.ResourceTypes.AnyAsync(t => t.Id == model.ResourceTypeId, ct)) throw new ValidationException("Материал түрін таңдаңыз.");
        if (model.TagIds.Count > 20) throw new ValidationException("Ең көбі 20 тег таңдауға болады.");
        if (await db.Tags.CountAsync(t => model.TagIds.Contains(t.Id), ct) != model.TagIds.Distinct().Count()) throw new ValidationException("Таңдалған тег табылмады.");
    }

    public async Task<Resource> CreateAsync(ResourceEditViewModel model, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!(user.IsInRole(AppRoles.Teacher) || user.IsInRole(AppRoles.Admin))) throw new UnauthorizedAccessException();
        await ValidateMetadataAsync(model, ct);
        if (model.File is null) throw new FileValidationException("PDF файлын таңдаңыз.");
        var stored = await storage.SaveAsync(model.File, ct);
        var resource = new Resource { OwnerId = ResourceAccess.UserId(user)!, Analysis = new ResourceAnalysis() };
        ApplyMetadata(resource, model);
        ApplyFile(resource, stored);
        resource.TextExtraction = new ResourceTextExtraction();
        resource.ProcessingJob = new ProcessingJob { FileHash = resource.FileHash };
        resource.SearchDocument = new ResourceSearchDocument { SearchText = await SearchTextAsync(resource, model.TagIds, ct) };
        db.Resources.Add(resource);
        try { await db.SaveChangesAsync(ct); }
        catch { TryDelete(stored.StoredFileName); throw; }
        return resource;
    }

    public async Task UpdateAsync(Resource resource, ResourceEditViewModel model, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (!ResourceAccess.CanManage(user, resource)) throw new UnauthorizedAccessException();
        await ValidateMetadataAsync(model, ct);
        if (string.IsNullOrEmpty(model.RowVersion) || !Convert.TryFromBase64String(model.RowVersion, new byte[8], out var versionLength) || versionLength != 8)
            throw new ValidationException("Материал нұсқасы дұрыс емес. Бетті жаңартып, қайта өңдеңіз.");
        db.Entry(resource).Property(r => r.RowVersion).OriginalValue = Convert.FromBase64String(model.RowVersion);
        StoredFile? replacement = null;
        var previousFile = resource.StoredFileName;
        if (model.File is not null) replacement = await storage.SaveAsync(model.File, ct);
        try
        {
            var requestedTags = model.TagIds.Distinct().ToHashSet();
            var removedTags = resource.ResourceTags.Where(t => !requestedTags.Contains(t.TagId)).ToList();
            db.ResourceTags.RemoveRange(removedTags);
            foreach (var tag in removedTags) resource.ResourceTags.Remove(tag);
            var existingTags = resource.ResourceTags.Select(t => t.TagId).ToHashSet();
            foreach (var tagId in requestedTags.Except(existingTags)) resource.ResourceTags.Add(new ResourceTag { TagId = tagId });
            var updatedTags = resource.ResourceTags;
            ApplyMetadata(resource, model);
            resource.ResourceTags = updatedTags;
            if (replacement is not null)
            {
                var repeatAnalysis = resource.Analysis?.Status is AnalysisStatus.Completed or AnalysisStatus.Pending or AnalysisStatus.Processing ||
                    await db.ProcessingJobs.AnyAsync(j => j.ResourceId == resource.Id && j.AnalyzeRequested, ct) ||
                    await db.ResourceEmbeddings.AnyAsync(e => e.ResourceId == resource.Id, ct);
                ApplyFile(resource, replacement);
                resource.ExtractedText = null;
                resource.Analysis ??= new ResourceAnalysis();
                resource.Analysis.Status = AnalysisStatus.NotAnalyzed;
                resource.Analysis.DetectedTopic = null; resource.Analysis.Summary = null;
                resource.Analysis.Difficulty = null; resource.Analysis.Keywords = null;
                resource.Analysis.Provider = null; resource.Analysis.Model = null;
                resource.Analysis.AnalyzedAtUtc = null; resource.Analysis.AnalyzedFileHash = null;
                resource.Analysis.ErrorCode = null;
                resource.Analysis.ErrorMessage = null; resource.Analysis.SuggestedSubjectId = null; resource.Analysis.SuggestedTopicId = null;
                resource.Analysis.AnalyzedCharacters = 0; resource.Analysis.SourceCharacters = 0;
                resource.Analysis.ProcessedPages = 0; resource.Analysis.TotalPages = 0; resource.Analysis.IsPartial = false;
                var extraction = await db.ResourceTextExtractions.Include(e => e.Pages).SingleOrDefaultAsync(e => e.ResourceId == resource.Id, ct);
                if (extraction is null) { extraction = new ResourceTextExtraction { ResourceId = resource.Id }; db.ResourceTextExtractions.Add(extraction); }
                db.ResourceTextPages.RemoveRange(extraction.Pages);
                extraction.Status = ExtractionStatus.Pending; extraction.ExtractedFileHash = null; extraction.ExtractedAtUtc = null;
                extraction.TotalPages = 0; extraction.ProcessedPages = 0; extraction.CharacterCount = 0; extraction.IsTruncated = false;
                extraction.ErrorCode = null; extraction.ErrorMessage = null;
                var embedding = await db.ResourceEmbeddings.FindAsync([resource.Id], ct);
                if (embedding is not null) db.ResourceEmbeddings.Remove(embedding);
                var job = await db.ProcessingJobs.FindAsync([resource.Id], ct);
                if (job is null) { job = new ProcessingJob { ResourceId = resource.Id }; db.ProcessingJobs.Add(job); }
                job.FileHash = resource.FileHash; job.Status = JobStatus.Pending; job.AnalyzeRequested = repeatAnalysis;
                job.Attempts = 0; job.LeaseToken = null; job.LeaseExpiresAtUtc = null;
                job.NotBeforeUtc = DateTime.UtcNow; job.UpdatedAtUtc = DateTime.UtcNow; job.LastErrorCode = null;
            }
            var document = await db.ResourceSearchDocuments.FindAsync([resource.Id], ct);
            if (document is null) { document = new ResourceSearchDocument { ResourceId = resource.Id }; db.ResourceSearchDocuments.Add(document); }
            document.SearchText = await SearchTextAsync(resource, model.TagIds, ct); document.UpdatedAtUtc = DateTime.UtcNow;
            resource.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch { if (replacement is not null) TryDelete(replacement.StoredFileName); throw; }
        if (replacement is not null) TryDelete(previousFile);
    }

    private static void ApplyMetadata(Resource resource, ResourceEditViewModel model)
    {
        resource.Title = model.Title.Trim(); resource.Description = model.Description.Trim();
        resource.SubjectId = model.SubjectId; resource.TopicId = model.TopicId;
        resource.ResourceTypeId = model.ResourceTypeId; resource.AccessLevel = model.AccessLevel; resource.Status = model.Status;
        resource.ResourceTags = model.TagIds.Distinct().Select(id => new ResourceTag { TagId = id }).ToList();
    }
    private static void ApplyFile(Resource resource, StoredFile file)
    {
        resource.StoredFileName = file.StoredFileName; resource.OriginalFileName = file.OriginalFileName;
        resource.FileSize = file.FileSize; resource.ContentType = file.ContentType;
        resource.Extension = file.Extension; resource.FileHash = file.FileHash;
    }
    private async Task<string> SearchTextAsync(Resource resource, List<int> tagIds, CancellationToken ct)
    {
        var names = await db.Tags.Where(t => tagIds.Contains(t.Id)).Select(t => t.Name).ToListAsync(ct);
        var text = resource.Title + "\n" + resource.Description + "\n" + string.Join("\n", names);
        if (resource.ExtractedText is not null && await db.ResourceTextExtractions.AnyAsync(e => e.ResourceId == resource.Id && e.ExtractedFileHash == resource.FileHash, ct)) text += "\n" + resource.ExtractedText;
        if (resource.Analysis?.Status == AnalysisStatus.Completed && resource.Analysis.AnalyzedFileHash == resource.FileHash)
            text += "\n" + resource.Analysis.Summary + "\n" + resource.Analysis.Keywords;
        return text;
    }
    private void TryDelete(string name)
    {
        try { storage.Delete(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogError(ex, "File cleanup failed for a stored resource."); }
    }
}
