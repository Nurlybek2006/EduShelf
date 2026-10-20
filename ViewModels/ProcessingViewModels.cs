using System.Text.Json;
using EduShelf.Models;

namespace EduShelf.ViewModels;

public class ProcessingPanelViewModel
{
    public string Status { get; set; } = "NotAnalyzed";
    public string StatusLabel { get; set; } = "Талдау орындалмаған";
    public bool IsConfigured { get; set; }
    public bool IsStale { get; set; }
    public string? Message { get; set; }
    public string? Summary { get; set; }
    public string? DetectedTopic { get; set; }
    public string? Difficulty { get; set; }
    public List<string> Keywords { get; set; } = [];
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public DateTime? AnalyzedAtUtc { get; set; }
    public bool HasSuggestion { get; set; }
    public string? SuggestedSubjectName { get; set; }
    public string? SuggestedTopicName { get; set; }
    public string ExtractionStatus { get; set; } = "Pending";
    public string ExtractionStatusLabel { get; set; } = "Мәтін алу кезекте";
    public string? ExtractionMessage { get; set; }
    public int ExtractedPages { get; set; }
    public int TotalPages { get; set; }
    public int ExtractedCharacters { get; set; }
    public int AnalyzedCharacters { get; set; }
    public int SourceCharacters { get; set; }
    public bool IsPartial { get; set; }
    public bool ExtractionTruncated { get; set; }

    public static ProcessingPanelViewModel From(Resource resource, bool isConfigured)
    {
        var analysis = resource.Analysis;
        var extraction = resource.TextExtraction;
        var current = analysis?.AnalyzedFileHash == resource.FileHash;
        var status = analysis?.Status ?? AnalysisStatus.NotAnalyzed;
        var stale = status == AnalysisStatus.Completed && !current;
        if (stale) status = AnalysisStatus.NotAnalyzed;
        if (!isConfigured && status is not (AnalysisStatus.Completed or AnalysisStatus.Processing or AnalysisStatus.Pending)) status = AnalysisStatus.NotConfigured;
        var result = new ProcessingPanelViewModel
        {
            IsConfigured = isConfigured, IsStale = stale, Status = status.ToString(), StatusLabel = StatusText(status),
            Message = status == AnalysisStatus.NotConfigured ? "AI қызметі бапталмаған" : analysis?.ErrorMessage,
            ExtractionStatus = (extraction?.Status ?? Models.ExtractionStatus.Pending).ToString(),
            ExtractionStatusLabel = extraction?.Status switch { Models.ExtractionStatus.Completed => "Мәтін алынды", Models.ExtractionStatus.Processing => "Мәтін өңделуде", Models.ExtractionStatus.NoText => "Оқылатын мәтін табылмады", Models.ExtractionStatus.Failed => "Мәтін алу қатесі", _ => "Мәтін алу кезекте" },
            ExtractionMessage = extraction?.ErrorMessage,
            ExtractedPages = extraction?.ProcessedPages ?? 0, TotalPages = extraction?.TotalPages ?? 0,
            ExtractedCharacters = extraction?.CharacterCount ?? 0, ExtractionTruncated = extraction?.IsTruncated ?? false
        };
        if (status == AnalysisStatus.Completed && current && analysis is not null)
        {
            result.Summary = analysis.Summary; result.DetectedTopic = analysis.DetectedTopic;
            result.Difficulty = analysis.Difficulty; result.Provider = analysis.Provider; result.Model = analysis.Model;
            result.AnalyzedAtUtc = analysis.AnalyzedAtUtc; result.IsPartial = analysis.IsPartial;
            result.AnalyzedCharacters = analysis.AnalyzedCharacters; result.SourceCharacters = analysis.SourceCharacters;
            result.HasSuggestion = analysis.SuggestedSubjectId.HasValue;
            result.SuggestedSubjectName = analysis.SuggestedSubject?.Name; result.SuggestedTopicName = analysis.SuggestedTopic?.Name;
            try { result.Keywords = JsonSerializer.Deserialize<List<string>>(analysis.Keywords ?? "[]") ?? []; }
            catch (JsonException) { result.Keywords = []; }
        }
        return result;
    }
    public static string StatusText(AnalysisStatus status) => status switch
    {
        AnalysisStatus.Pending => "Кезекте", AnalysisStatus.Processing => "Өңделуде", AnalysisStatus.Completed => "Дайын",
        AnalysisStatus.Failed => "Қате", AnalysisStatus.NotConfigured => "AI қызметі бапталмаған", _ => "Талдау орындалмаған"
    };
}
public class RelatedResourcesViewModel
{
    public List<ResourceCardViewModel> Items { get; set; } = [];
    public bool IsSemantic { get; set; }
    public string Heading => IsSemantic ? "Мағынасы ұқсас материалдар" : "Тақырып бойынша ұқсас";
}
