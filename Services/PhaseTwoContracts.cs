using EduShelf.Models;

namespace EduShelf.Services;

// Phase two extension points. No provider is registered or invoked in phase one.
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
