using System.ComponentModel.DataAnnotations;

namespace EduShelf.ViewModels;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Электрондық поштаны енгізіңіз.")]
    [EmailAddress(ErrorMessage = "Электрондық пошта пішімі дұрыс емес.")]
    [StringLength(256, ErrorMessage = "Электрондық пошта 256 таңбадан аспауы керек.")]
    [Display(Name = "Электрондық пошта")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Құпиясөзді енгізіңіз.")]
    [StringLength(100, ErrorMessage = "Құпиясөз 100 таңбадан аспауы керек.")]
    [DataType(DataType.Password)]
    [Display(Name = "Құпиясөз")]
    public string Password { get; set; } = "";

    [Display(Name = "Мені есте сақтау")]
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public sealed class RegisterViewModel
{
    [Required(ErrorMessage = "Аты-жөніңізді енгізіңіз.")]
    [StringLength(120, ErrorMessage = "Аты-жөніңіз 120 таңбадан аспауы керек.")]
    [Display(Name = "Аты-жөніңіз")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Электрондық поштаны енгізіңіз.")]
    [EmailAddress(ErrorMessage = "Электрондық пошта пішімі дұрыс емес.")]
    [StringLength(256, ErrorMessage = "Электрондық пошта 256 таңбадан аспауы керек.")]
    [Display(Name = "Электрондық пошта")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Құпиясөзді енгізіңіз.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Құпиясөз 8–100 таңбадан тұруы керек.")]
    [DataType(DataType.Password)]
    [Display(Name = "Құпиясөз")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Құпиясөзді қайталаңыз.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Құпиясөздер сәйкес емес.")]
    [Display(Name = "Құпиясөзді қайталау")]
    public string ConfirmPassword { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
