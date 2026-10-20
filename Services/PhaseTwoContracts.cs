using EduShelf.Models;

namespace EduShelf.Services;

// Implementations perform bounded extraction, durable analysis scheduling and cached recommendations.
public interface IResourceTextExtractor
{
    Task<string?> ExtractAsync(Stream pdf, CancellationToken cancellationToken = default);
}
public interface IResourceAnalysisService
{
    Task<ResourceAnalysis> AnalyzeAsync(int resourceId, CancellationToken cancellationToken = default);
}
public interface IRelatedResourcesService
{
    Task<IReadOnlyList<int>> FindRelatedAsync(int resourceId, int limit = 6, CancellationToken cancellationToken = default);
}
