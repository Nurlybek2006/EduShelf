using System.Text;
using EduShelf.Data;
using EduShelf.Models;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Services;

public interface IResourceSearchIndexer
{
    Task UpdateAsync(int resourceId, CancellationToken cancellationToken = default);
}
public class ResourceSearchIndexer(ApplicationDbContext db) : IResourceSearchIndexer
{
    public async Task UpdateAsync(int resourceId, CancellationToken cancellationToken = default)
    {
        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var resource = await db.Resources.FromSqlInterpolated($"SELECT * FROM [Resources] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={resourceId}").AsNoTracking().Include(r => r.ResourceTags).ThenInclude(t => t.Tag).Include(r => r.Analysis)
            .Include(r => r.TextExtraction).SingleOrDefaultAsync(r => r.Id == resourceId, cancellationToken);
        if (resource is null) return;
        var text = new StringBuilder(resource.Title).Append('\n').Append(resource.Description);
        foreach (var tag in resource.ResourceTags) text.Append('\n').Append(tag.Tag.Name);
        if (resource.TextExtraction?.ExtractedFileHash == resource.FileHash) text.Append('\n').Append(resource.ExtractedText);
        if (resource.Analysis?.Status == AnalysisStatus.Completed && resource.Analysis.AnalyzedFileHash == resource.FileHash)
            text.Append('\n').Append(resource.Analysis.Summary).Append('\n').Append(resource.Analysis.Keywords).Append('\n').Append(resource.Analysis.DetectedTopic);
        var document = await db.ResourceSearchDocuments.FindAsync([resourceId], cancellationToken);
        if (document is null) { document = new ResourceSearchDocument { ResourceId = resourceId }; db.ResourceSearchDocuments.Add(document); }
        document.SearchText = text.ToString(); document.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }
}
