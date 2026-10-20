using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services.Processing;
using EduShelf.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduShelf.Services;

public class RecommendationOptions
{
    [Range(5, 1000)] public int MaxCandidates { get; set; } = 200;
    [Range(3, 5)] public int Count { get; set; } = 4;
}
public class RelatedResourcesService(ApplicationDbContext db, ResourceService resources, IOptions<RecommendationOptions> options, IOptions<AiOptions> ai) : IRelatedResourcesService
{
    public async Task<RelatedResourcesViewModel> GetAsync(int resourceId, ClaimsPrincipal user, CancellationToken ct = default)
    {
        var source = await db.Resources.AsNoTracking().VisibleTo(user).Include(r => r.Embedding).Include(r => r.ResourceTags)
            .SingleOrDefaultAsync(r => r.Id == resourceId, ct);
        if (source is null) throw new KeyNotFoundException();
        var visible = db.Resources.AsNoTracking().VisibleTo(user, catalogOnly: true).Where(r => r.Id != resourceId);
        var tagIds = source.ResourceTags.Select(t => t.TagId).ToArray();
        var embedding = source.Embedding;
        if (ai.Value.IsConfigured && ai.Value.EnableEmbeddings && embedding is not null && embedding.FileHash == source.FileHash &&
            embedding.Model == ai.Value.EmbeddingModel && embedding.Dimensions == ai.Value.EmbeddingDimensions && embedding.Provider == ai.Value.Provider)
        {
            var vector = ParseVector(embedding.VectorJson, embedding.Dimensions);
            if (vector is not null)
            {
                var candidates = await visible.Where(r => r.Embedding != null && r.Embedding.FileHash == r.FileHash &&
                    r.Embedding.Provider == embedding.Provider && r.Embedding.Model == embedding.Model && r.Embedding.Dimensions == embedding.Dimensions)
                    .OrderByDescending(r => r.SubjectId == source.SubjectId).ThenByDescending(r => r.Id)
                    .Select(r => new { r.Id, r.Embedding!.VectorJson }).Take(options.Value.MaxCandidates).ToListAsync(ct);
                var selected = candidates.Select(c => new { c.Id, Vector = ParseVector(c.VectorJson, embedding.Dimensions) })
                    .Where(c => c.Vector is not null).Select(c => new { c.Id, Score = CosineSimilarity(vector, c.Vector!) })
                    .OrderByDescending(c => c.Score).ThenBy(c => c.Id).Take(options.Value.Count).Select(c => c.Id).ToArray();
                if (selected.Length != 0)
                {
                    var cards = await resources.CardListAsync(visible.Where(r => selected.Contains(r.Id)), user, ct);
                    return new() { IsSemantic = true, Items = cards.OrderBy(c => Array.IndexOf(selected, c.Id)).ToList() };
                }
            }
        }
        var related = visible.Where(r => r.SubjectId == source.SubjectId || (source.TopicId != null && r.TopicId == source.TopicId) || r.ResourceTags.Any(t => tagIds.Contains(t.TagId)))
            .OrderByDescending(r => source.TopicId != null && r.TopicId == source.TopicId).ThenByDescending(r => r.ResourceTags.Count(t => tagIds.Contains(t.TagId)))
            .ThenByDescending(r => r.SubjectId == source.SubjectId).ThenByDescending(r => r.Id).Take(options.Value.Count);
        var items = await resources.CardListAsync(related, user, ct);
        return new() { Items = items, IsSemantic = false };
    }

    // Legacy API is deliberately anonymous: it must never reveal authenticated-only resources.
    public async Task<IReadOnlyList<int>> FindRelatedAsync(int resourceId, int limit = 6, CancellationToken cancellationToken = default)
        => (await GetAsync(resourceId, new ClaimsPrincipal(new ClaimsIdentity()), cancellationToken)).Items.Take(Math.Clamp(limit, 1, 5)).Select(c => c.Id).ToArray();
    public static double CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length != right.Length || left.Length == 0) throw new ArgumentException("Vector dimensions must match.");
        double dot = 0, a = 0, b = 0;
        for (var i = 0; i < left.Length; i++) { if (!float.IsFinite(left[i]) || !float.IsFinite(right[i])) throw new ArgumentException("Vectors must be finite."); dot += (double)left[i] * right[i]; a += (double)left[i] * left[i]; b += (double)right[i] * right[i]; }
        return a <= 0 || b <= 0 ? 0 : Math.Clamp(dot / Math.Sqrt(a * b), -1, 1);
    }
    private static float[]? ParseVector(string json, int dimensions)
    {
        if (dimensions is < 1 or > 3072 || json.Length > 200000) return null;
        try { var value = JsonSerializer.Deserialize<float[]>(json); return value?.Length == dimensions && value.All(float.IsFinite) && value.Any(v => v != 0) ? value : null; }
        catch (JsonException) { return null; }
    }
}
