using System.ComponentModel.DataAnnotations;
using EduShelf.Data;
using EduShelf.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduShelf.Services;

public class SearchOptions
{
    public string Mode { get; set; } = "Auto";
    public bool AllowLikeFallback { get; set; } = true;
}
public class FullTextHit
{
    public int ResourceId { get; set; }
    public int Rank { get; set; }
}
public record CatalogSearchResult(IQueryable<Resource> Query, string Mode);

public class CatalogSearchService(ApplicationDbContext db, IOptions<SearchOptions> options, ILogger<CatalogSearchService> logger)
{
    public async Task<bool> IsFullTextAvailableAsync(CancellationToken ct = default)
    {
        var available = await db.Database.SqlQuery<int>($"SELECT CASE WHEN FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1 AND OBJECTPROPERTY(OBJECT_ID(N'dbo.ResourceSearchDocuments'), 'TableHasActiveFulltextIndex') = 1 THEN 1 ELSE 0 END AS [Value]")
            .SingleAsync(ct);
        return available == 1;
    }

    public async Task<CatalogSearchResult> ApplyAsync(IQueryable<Resource> resources, string? search, string sort, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(search)) return new(Order(resources, sort), "Мәтін бойынша іздеу");
        search = search.Trim()[..Math.Min(search.Trim().Length, 200)];
        var useFullText = !options.Value.Mode.Equals("Like", StringComparison.OrdinalIgnoreCase) && await IsFullTextAvailableAsync(ct);
        if (useFullText)
        {
            try
            {
                // FREETEXT treats user input as text rather than CONTAINS query syntax.
                var hits = db.Database.SqlQuery<FullTextHit>($"SELECT [KEY] AS ResourceId, [RANK] AS Rank FROM FREETEXTTABLE(dbo.ResourceSearchDocuments, SearchText, {search}, LANGUAGE 0)");
                // Execute a small probe so an unavailable index/word breaker can fall back safely.
                _ = await hits.Take(1).ToListAsync(ct);
                var result = resources.Where(r => hits.Any(h => h.ResourceId == r.Id) || r.Title.Contains(search) || r.ResourceTags.Any(t => t.Tag.Name.Contains(search)));
                var ordered = result.OrderByDescending(r => r.Title == search).ThenByDescending(r => r.Title.Contains(search));
                IQueryable<Resource> query = sort switch
                {
                    "popular" => ordered.ThenByDescending(r => r.Views.Count).ThenByDescending(r => r.Id),
                    "title" => ordered.ThenBy(r => r.Title).ThenBy(r => r.Id),
                    _ => ordered.ThenByDescending(r => hits.Where(h => h.ResourceId == r.Id).Select(h => (int?)h.Rank).FirstOrDefault() ?? 0).ThenByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
                };
                return new(query, "SQL Server Full-Text · neutral");
            }
            catch (SqlException ex) when (ex.Number is 7601 or 7603 or 7609 or 7619 or 7645 or 7635 or 30053)
            {
                logger.LogWarning("Full-text search unavailable; SQL error {Code}.", ex.Number);
            }
        }
        if (!options.Value.AllowLikeFallback && !options.Value.Mode.Equals("Like", StringComparison.OrdinalIgnoreCase))
            return new(resources.Where(_ => false), "Full-Text қолжетімсіз. Іздеу баптауын әкімші тексеруі керек.");
        // EF translates Contains to parameterized SQL. All filtering, ranking and paging stay in SQL Server.
        var filtered = resources.Where(r => r.Title.Contains(search) || r.Description.Contains(search) ||
            r.ResourceTags.Any(t => t.Tag.Name.Contains(search)) ||
            (r.TextExtraction != null && r.TextExtraction.ExtractedFileHash == r.FileHash && r.ExtractedText != null && r.ExtractedText.Contains(search)) ||
            (r.Analysis != null && r.Analysis.Status == AnalysisStatus.Completed && r.Analysis.AnalyzedFileHash == r.FileHash &&
                ((r.Analysis.Keywords != null && r.Analysis.Keywords.Contains(search)) || (r.Analysis.Summary != null && r.Analysis.Summary.Contains(search)))));
        var ranked = filtered.OrderByDescending(r => r.Title == search).ThenByDescending(r => r.Title.Contains(search));
        var fallback = sort switch
        {
            "popular" => ranked.ThenByDescending(r => r.Views.Count).ThenByDescending(r => r.Id),
            "title" => ranked.ThenBy(r => r.Title).ThenBy(r => r.Id),
            _ => ranked.ThenByDescending(r => r.Description.Contains(search)).ThenByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
        };
        return new(fallback, "Мәтін бойынша іздеу · SQL fallback");
    }
    private static IQueryable<Resource> Order(IQueryable<Resource> resources, string sort) => sort switch
    {
        "popular" => resources.OrderByDescending(r => r.Views.Count).ThenByDescending(r => r.Id),
        "title" => resources.OrderBy(r => r.Title).ThenBy(r => r.Id),
        _ => resources.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id)
    };
}
