using System.ComponentModel.DataAnnotations;

namespace EduShelf.Models;

public enum ExtractionStatus { Pending, Processing, Completed, Failed, NoText }
public enum JobStatus { Pending, Processing, Completed, Failed }

public class ResourceTextExtraction
{
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public ExtractionStatus Status { get; set; } = ExtractionStatus.Pending;
    [MaxLength(64)] public string? ExtractedFileHash { get; set; }
    public int TotalPages { get; set; }
    public int ProcessedPages { get; set; }
    public int CharacterCount { get; set; }
    public bool IsTruncated { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
    [MaxLength(500)] public string? ErrorMessage { get; set; }
    public DateTime? ExtractedAtUtc { get; set; }
    public ICollection<ResourceTextPage> Pages { get; set; } = [];
}
public class ResourceTextPage
{
    public int ResourceId { get; set; }
    public ResourceTextExtraction Extraction { get; set; } = null!;
    public int PageNumber { get; set; }
    public string Text { get; set; } = "";
}
public class ResourceEmbedding
{
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    [MaxLength(100)] public string Provider { get; set; } = "OpenAI";
    [MaxLength(100)] public string Model { get; set; } = "";
    public int Dimensions { get; set; }
    [MaxLength(64)] public string FileHash { get; set; } = "";
    public string VectorJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int CoveredCharacters { get; set; }
    public bool IsPartial { get; set; }
}
public class ProcessingJob
{
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    [MaxLength(64)] public string FileHash { get; set; } = "";
    public bool AnalyzeRequested { get; set; }
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public int Attempts { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseExpiresAtUtc { get; set; }
    public DateTime NotBeforeUtc { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string? LastErrorCode { get; set; }
}
public class ResourceSearchDocument
{
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public string SearchText { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
public class PageBookmark
{
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public int PageNumber { get; set; }
    [MaxLength(64)] public string FileHash { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
