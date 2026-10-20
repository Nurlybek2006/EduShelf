using EduShelf.Data;
using EduShelf.Models;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Services.Processing;

/// <summary>The public analysis service durably requests work; only the BackgroundService invokes the provider.</summary>
public sealed class QueuedResourceAnalysisService(ApplicationDbContext db, IProcessingQueue queue) : IResourceAnalysisService
{
    public async Task<ResourceAnalysis> AnalyzeAsync(int resourceId, CancellationToken cancellationToken = default)
    {
        await queue.EnqueueAsync(resourceId, true, cancellationToken);
        return await db.ResourceAnalyses.AsNoTracking().SingleAsync(x => x.ResourceId == resourceId, cancellationToken);
    }
}
