using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduShelf.Services;

public class ResourceService(ApplicationDbContext db, IFileStorage storage, IOptions<StorageOptions> storageOptions, ILogger<ResourceService> logger)
{
    public static IQueryable<ResourceCardViewModel> Cards(IQueryable<Resource> query, string? userId) => query.Select(r => new ResourceCardViewModel
    {
        Id = r.Id, Title = r.Title, Description = r.Description, SubjectName = r.Subject.Name,
        TypeName = r.ResourceType.Name, OwnerName = r.Owner.FullName, ViewCount = r.Views.Count,
        DownloadCount = r.Downloads.Count, FileSize = r.FileSize, Status = r.Status, AccessLevel = r.AccessLevel
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
        if (!string.IsNullOrEmpty(query.Search)) resources = resources.Where(r => r.Title.Contains(query.Search) || r.Description.Contains(query.Search) || r.ResourceTags.Any(t => t.Tag.Name.Contains(query.Search)));
        if (query.SubjectId.HasValue) resources = resources.Where(r => r.SubjectId == query.SubjectId);
        if (query.TopicId.HasValue) resources = resources.Where(r => r.TopicId == query.TopicId);
        if (query.TypeId.HasValue) resources = resources.Where(r => r.ResourceTypeId == query.TypeId);
        var count = await resources.CountAsync(ct);
        var pages = (int)Math.Ceiling(count / (double)CatalogQuery.PageSize);
        query.Page = Math.Min(query.Page, Math.Max(1, pages));
        resources = query.Sort switch
        {
            "popular" => resources.OrderByDescending(r => r.Views.Count).ThenByDescending(r => r.Id),
            "title" => resources.OrderBy(r => r.Title).ThenBy(r => r.Id),
            _ => resources.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
        };
        return new CatalogViewModel
        {
            Query = query, TotalCount = count, PageCount = pages,
            Items = await CardListAsync(resources.Skip((query.Page - 1) * CatalogQuery.PageSize).Take(CatalogQuery.PageSize), user, ct),
            Subjects = await db.Subjects.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
            Topics = await db.Topics.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct),
            ResourceTypes = await db.ResourceTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct)
        };
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
                ApplyFile(resource, replacement);
                resource.ExtractedText = null;
                resource.Analysis ??= new ResourceAnalysis();
                resource.Analysis.Status = AnalysisStatus.NotAnalyzed;
                resource.Analysis.DetectedTopic = null; resource.Analysis.Summary = null;
                resource.Analysis.Difficulty = null; resource.Analysis.Keywords = null;
                resource.Analysis.Provider = null; resource.Analysis.Model = null;
                resource.Analysis.AnalyzedAtUtc = null; resource.Analysis.AnalyzedFileHash = null;
                resource.Analysis.ErrorCode = null;
            }
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
    private void TryDelete(string name)
    {
        try { storage.Delete(name); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogError(ex, "File cleanup failed for a stored resource."); }
    }
}
