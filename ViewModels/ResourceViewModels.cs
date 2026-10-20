using System.ComponentModel.DataAnnotations;
using EduShelf.Models;

namespace EduShelf.ViewModels;

public class CatalogQuery
{
    [StringLength(200)] public string? Search { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? TypeId { get; set; }
    public int? TagId { get; set; }
    public string? Difficulty { get; set; }
    public string Sort { get; set; } = "new";
    public int Page { get; set; } = 1;
    public const int PageSize = 12;
}
public class ResourceCardViewModel
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public int ViewCount { get; set; }
    public int DownloadCount { get; set; }
    public long FileSize { get; set; }
    public bool IsFavorite { get; set; }
    public AccessLevel AccessLevel { get; set; }
    public ResourceStatus Status { get; set; }
    public string? Difficulty { get; set; }
    public string? Snippet { get; set; }
}
public class HomeViewModel
{
    public int ResourceCount { get; set; }
    public int SubjectCount { get; set; }
    public int DownloadCount { get; set; }
    public List<ResourceCardViewModel> Latest { get; set; } = [];
    public List<ResourceCardViewModel> Popular { get; set; } = [];
    public List<Subject> Subjects { get; set; } = [];
}
public class CatalogViewModel
{
    public CatalogQuery Query { get; set; } = new();
    public List<ResourceCardViewModel> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageCount { get; set; }
    public List<Subject> Subjects { get; set; } = [];
    public List<Topic> Topics { get; set; } = [];
    public List<ResourceType> ResourceTypes { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
    public string SearchMode { get; set; } = "Мәтін бойынша іздеу";
}
public class ResourceDetailsViewModel
{
    public Resource Resource { get; set; } = null!;
    public bool IsFavorite { get; set; }
    public bool CanEdit { get; set; }
    public bool FileExists { get; set; }
    public int ViewCount { get; set; }
    public int DownloadCount { get; set; }
    public ProcessingPanelViewModel Processing { get; set; } = new();
    public RelatedResourcesViewModel Recommendations { get; set; } = new();
    public int? BookmarkPage { get; set; }
}
public class ResourceEditViewModel
{
    public int? Id { get; set; }
    public string? RowVersion { get; set; }
    [Required(ErrorMessage = "Материал атауын енгізіңіз."), StringLength(200, ErrorMessage = "Атау 200 таңбадан аспауы керек.")]
    public string Title { get; set; } = "";
    [Required(ErrorMessage = "Сипаттаманы енгізіңіз."), StringLength(4000, ErrorMessage = "Сипаттама 4000 таңбадан аспауы керек.")]
    public string Description { get; set; } = "";
    [Range(1, int.MaxValue, ErrorMessage = "Пәнді таңдаңыз.")] public int SubjectId { get; set; }
    public int? TopicId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Материал түрін таңдаңыз.")] public int ResourceTypeId { get; set; }
    public List<int> TagIds { get; set; } = [];
    [EnumDataType(typeof(AccessLevel), ErrorMessage = "Қолжетімділік мәні дұрыс емес.")] public AccessLevel AccessLevel { get; set; }
    [EnumDataType(typeof(ResourceStatus), ErrorMessage = "Материал күйі дұрыс емес.")] public ResourceStatus Status { get; set; }
    public IFormFile? File { get; set; }
    public string? ExistingFileName { get; set; }
    public List<Subject> Subjects { get; set; } = [];
    public List<Topic> Topics { get; set; } = [];
    public List<ResourceType> ResourceTypes { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
    public int MaxFileSizeMb { get; set; }
}
public class PersonalLibraryViewModel
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ResourceCardViewModel> Items { get; set; } = [];
}
public class HistoryViewModel
{
    public List<HistoryItemViewModel> Items { get; set; } = [];
}
public class HistoryItemViewModel
{
    public int ResourceId { get; set; }
    public string Title { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
}
public class AnalyticsViewModel
{
    public int ResourceCount { get; set; }
    public int ViewCount { get; set; }
    public int DownloadCount { get; set; }
    public List<ResourceCardViewModel> Items { get; set; } = [];
}
