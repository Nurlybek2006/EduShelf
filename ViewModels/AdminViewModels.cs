using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduShelf.ViewModels;

public sealed record LookupRowViewModel(int Id, string Name, string? ParentName = null);

public sealed class LookupListViewModel
{
    public string Kind { get; set; } = "subjects";
    public string Title { get; set; } = "Пәндер";
    public IReadOnlyList<LookupRowViewModel> Items { get; set; } = [];
}

public sealed class LookupEditViewModel
{
    public int Id { get; set; }
    public string Kind { get; set; } = "subjects";
    public string Title { get; set; } = "Пән";

    [Required(ErrorMessage = "Атауын енгізіңіз.")]
    [StringLength(150, ErrorMessage = "Атауы 150 таңбадан аспауы керек.")]
    [Display(Name = "Атауы")]
    public string Name { get; set; } = "";

    [Display(Name = "Пән")]
    public int? SubjectId { get; set; }
    public IReadOnlyList<SelectListItem> Subjects { get; set; } = [];
}

public sealed class UserRoleViewModel
{
    public string Id { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public IReadOnlyList<string> Roles { get; set; } = [];
}

public sealed class UsersListViewModel
{
    public string? Search { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<UserRoleViewModel> Users { get; set; } = [];
}

public sealed class ChangeRoleViewModel
{
    [Required(ErrorMessage = "Қолданушыны таңдаңыз.")]
    public string UserId { get; set; } = "";
    [Required(ErrorMessage = "Рөлді таңдаңыз.")]
    public string Role { get; set; } = "";
}
