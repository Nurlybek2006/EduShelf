using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace EduShelf.Models;

public static class AppRoles
{
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Admin = "Admin";
    public static readonly string[] All = [Student, Teacher, Admin];
}

public class ApplicationUser : IdentityUser
{
    [MaxLength(120)] public string FullName { get; set; } = "";
}

public enum AccessLevel { Public, Authenticated }
public enum ResourceStatus { Draft, Published, Archived }
public enum AnalysisStatus { NotAnalyzed, Pending, Completed, Failed, Processing, NotConfigured }

public class Subject
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(500)] public string? Description { get; set; }
    public ICollection<Topic> Topics { get; set; } = [];
}
public class Topic
{
    public int Id { get; set; }
    [MaxLength(120)] public string Name { get; set; } = "";
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
}
public class ResourceType
{
    public int Id { get; set; }
    [MaxLength(80)] public string Name { get; set; } = "";
}
public class Tag
{
    public int Id { get; set; }
    [MaxLength(60)] public string Name { get; set; } = "";
}
public class Resource
{
    public int Id { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(4000)] public string Description { get; set; } = "";
    public int SubjectId { get; set; }
    public Subject Subject { get; set; } = null!;
    public int? TopicId { get; set; }
    public Topic? Topic { get; set; }
    public int ResourceTypeId { get; set; }
    public ResourceType ResourceType { get; set; } = null!;
    public string OwnerId { get; set; } = "";
    public ApplicationUser Owner { get; set; } = null!;
    [MaxLength(255)] public string OriginalFileName { get; set; } = "";
    [MaxLength(80)] public string StoredFileName { get; set; } = "";
    [MaxLength(100)] public string ContentType { get; set; } = "application/pdf";
    public long FileSize { get; set; }
    [MaxLength(12)] public string Extension { get; set; } = ".pdf";
    [MaxLength(64)] public string FileHash { get; set; } = "";
    public string? ExtractedText { get; set; }
    public AccessLevel AccessLevel { get; set; }
    public ResourceStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public ICollection<ResourceTag> ResourceTags { get; set; } = [];
    public ICollection<ResourceView> Views { get; set; } = [];
    public ICollection<ResourceDownload> Downloads { get; set; } = [];
    public ResourceAnalysis? Analysis { get; set; }
    public ResourceTextExtraction? TextExtraction { get; set; }
    public ResourceEmbedding? Embedding { get; set; }
    public ProcessingJob? ProcessingJob { get; set; }
    public ResourceSearchDocument? SearchDocument { get; set; }
}
public class ResourceTag
{
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
public class Favorite
{
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public class ResourceView
{
    public long Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
public class ResourceDownload
{
    public long Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
}
public class ResourceAnalysis
{
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public AnalysisStatus Status { get; set; } = AnalysisStatus.NotAnalyzed;
    [MaxLength(200)] public string? DetectedTopic { get; set; }
    [MaxLength(4000)] public string? Summary { get; set; }
    [MaxLength(50)] public string? Difficulty { get; set; }
    [MaxLength(2000)] public string? Keywords { get; set; }
    [MaxLength(100)] public string? Provider { get; set; }
    [MaxLength(100)] public string? Model { get; set; }
    public DateTime? AnalyzedAtUtc { get; set; }
    [MaxLength(100)] public string? ErrorCode { get; set; }
    [MaxLength(64)] public string? AnalyzedFileHash { get; set; }
    public int? SuggestedSubjectId { get; set; }
    public Subject? SuggestedSubject { get; set; }
    public int? SuggestedTopicId { get; set; }
    public Topic? SuggestedTopic { get; set; }
    [MaxLength(500)] public string? ErrorMessage { get; set; }
    public int AnalyzedCharacters { get; set; }
    public int SourceCharacters { get; set; }
    public int ProcessedPages { get; set; }
    public int TotalPages { get; set; }
    public bool IsPartial { get; set; }
}
