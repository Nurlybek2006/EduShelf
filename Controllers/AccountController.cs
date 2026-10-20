using EduShelf.Data;
using EduShelf.Models;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Controllers;

public sealed class AccountController(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db,
    ILogger<AccountController> logger) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid) return View(model);

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not null)
        {
            var result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
            if (result.Succeeded) return LocalRedirect(model.ReturnUrl!);
            if (result.IsLockedOut)
            {
                ModelState.AddModelError("", "Кіру уақытша бұғатталды. Біраз уақыттан кейін қайталап көріңіз.");
                return View(model);
            }
        }

        ModelState.AddModelError("", "Электрондық пошта немесе құпиясөз дұрыс емес.");
        return View(model);
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register(string? returnUrl = null)
        => View(new RegisterViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [AllowAnonymous, HttpPost]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        model.FullName = model.FullName?.Trim() ?? "";
        model.Email = model.Email?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(model.FullName))
            ModelState.AddModelError(nameof(model.FullName), "Аты-жөніңізді енгізіңіз.");
        if (!ModelState.IsValid) return View(model);

        ApplicationUser? registeredUser = null;
        var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var user = new ApplicationUser { FullName = model.FullName, UserName = model.Email, Email = model.Email };
            var createResult = await userManager.CreateAsync(user, model.Password);
            if (!createResult.Succeeded) return createResult;

            // The public form never controls roles; registration always grants Student only.
            var roleResult = await userManager.AddToRoleAsync(user, AppRoles.Student);
            if (!roleResult.Succeeded)
            {
                logger.LogWarning("Student role assignment failed during registration.");
                return IdentityResult.Failed(new IdentityError { Code = "RegistrationFailed" });
            }

            await transaction.CommitAsync();
            registeredUser = user;
            return IdentityResult.Success;
        });

        if (result.Succeeded && registeredUser is not null)
        {
            await signInManager.SignInAsync(registeredUser, isPersistent: false);
            return LocalRedirect(model.ReturnUrl!);
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", LocalizeIdentityError(error.Code));
        return View(model);
    }

    [Authorize, HttpPost]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private string SafeReturnUrl(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : Url.Content("~/");

    private static string LocalizeIdentityError(string code) => code switch
    {
        "DuplicateEmail" or "DuplicateUserName" => "Бұл электрондық пошта тіркелген. Аккаунтыңызға кіріңіз.",
        "InvalidEmail" or "InvalidUserName" => "Электрондық пошта пішімін тексеріңіз.",
        "PasswordTooShort" => "Құпиясөз кемінде 8 таңбадан тұруы керек.",
        "PasswordRequiresDigit" => "Құпиясөзде кемінде бір сан болуы керек.",
        "PasswordRequiresLower" => "Құпиясөзде кемінде бір кіші латын әрпі болуы керек.",
        "PasswordRequiresUpper" => "Құпиясөзде кемінде бір бас латын әрпі болуы керек.",
        "PasswordRequiresNonAlphanumeric" => "Құпиясөзде кемінде бір арнайы таңба болуы керек.",
        "PasswordRequiresUniqueChars" => "Құпиясөзде әртүрлі таңбалар қолданыңыз.",
        _ => "Тіркелу сәтсіз аяқталды. Деректерді тексеріп, қайталап көріңіз."
    };
}
