using System.Data.Common;
using System.Security.Claims;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Controllers;

[Authorize(Roles = AppRoles.Admin)]
public sealed class AdminController(
    ApplicationDbContext db,
    UserAdministrationService userAdministration,
    SignInManager<ApplicationUser> signInManager,
    ILogger<AdminController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Lookups(string kind = "subjects")
    {
        if (!IsKind(kind)) return NotFound();
        IReadOnlyList<LookupRowViewModel> rows = kind switch
        {
            "subjects" => await db.Subjects.AsNoTracking().OrderBy(x => x.Name).Select(x => new LookupRowViewModel(x.Id, x.Name, null)).ToListAsync(),
            "topics" => await db.Topics.AsNoTracking().OrderBy(x => x.Subject.Name).ThenBy(x => x.Name).Select(x => new LookupRowViewModel(x.Id, x.Name, x.Subject.Name)).ToListAsync(),
            "types" => await db.ResourceTypes.AsNoTracking().OrderBy(x => x.Name).Select(x => new LookupRowViewModel(x.Id, x.Name, null)).ToListAsync(),
            _ => await db.Tags.AsNoTracking().OrderBy(x => x.Name).Select(x => new LookupRowViewModel(x.Id, x.Name, null)).ToListAsync()
        };
        return View(new LookupListViewModel { Kind = kind, Title = PluralTitle(kind), Items = rows });
    }

    [HttpGet]
    public async Task<IActionResult> EditLookup(string kind, int id = 0)
    {
        if (!IsKind(kind) || id < 0) return NotFound();
        LookupEditViewModel? model;
        if (id == 0) model = new() { Kind = kind };
        else model = kind switch
        {
            "subjects" => await db.Subjects.Where(x => x.Id == id).Select(x => new LookupEditViewModel { Id = x.Id, Name = x.Name, Kind = kind }).SingleOrDefaultAsync(),
            "topics" => await db.Topics.Where(x => x.Id == id).Select(x => new LookupEditViewModel { Id = x.Id, Name = x.Name, SubjectId = x.SubjectId, Kind = kind }).SingleOrDefaultAsync(),
            "types" => await db.ResourceTypes.Where(x => x.Id == id).Select(x => new LookupEditViewModel { Id = x.Id, Name = x.Name, Kind = kind }).SingleOrDefaultAsync(),
            _ => await db.Tags.Where(x => x.Id == id).Select(x => new LookupEditViewModel { Id = x.Id, Name = x.Name, Kind = kind }).SingleOrDefaultAsync()
        };
        if (model is null) return NotFound();
        await PopulateLookupAsync(model);
        return View(model);
    }

    [HttpPost]
    public async Task<IActionResult> EditLookup(LookupEditViewModel model)
    {
        if (!IsKind(model.Kind) || model.Id < 0) return NotFound();
        model.Name = model.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "Атауын енгізіңіз.");
        var maximumLength = model.Kind switch { "subjects" => 100, "topics" => 120, "types" => 80, _ => 60 };
        if (model.Name.Length > maximumLength) ModelState.AddModelError(nameof(model.Name), $"Атауы {maximumLength} таңбадан аспауы керек.");
        if (model.Kind == "topics" && (!model.SubjectId.HasValue || !await db.Subjects.AnyAsync(x => x.Id == model.SubjectId)))
            ModelState.AddModelError(nameof(model.SubjectId), "Тізімнен пәнді таңдаңыз.");

        var duplicate = model.Kind switch
        {
            "subjects" => await db.Subjects.AnyAsync(x => x.Id != model.Id && x.Name == model.Name),
            "topics" => await db.Topics.AnyAsync(x => x.Id != model.Id && x.Name == model.Name && x.SubjectId == model.SubjectId),
            "types" => await db.ResourceTypes.AnyAsync(x => x.Id != model.Id && x.Name == model.Name),
            _ => await db.Tags.AnyAsync(x => x.Id != model.Id && x.Name == model.Name)
        };
        if (duplicate) ModelState.AddModelError(nameof(model.Name), "Осындай атау бұрын қосылған.");
        if (model.Kind == "topics" && model.Id > 0 && await db.Resources.AnyAsync(x => x.TopicId == model.Id && x.SubjectId != model.SubjectId))
            ModelState.AddModelError(nameof(model.SubjectId), "Тақырып материалдарда қолданылған. Оның пәнін өзгертуге болмайды.");

        if (!ModelState.IsValid)
        {
            await PopulateLookupAsync(model);
            return View(model);
        }

        switch (model.Kind)
        {
            case "subjects":
                var subject = model.Id == 0 ? new Subject() : await db.Subjects.FindAsync(model.Id);
                if (subject is null) return NotFound();
                subject.Name = model.Name;
                if (model.Id == 0) db.Subjects.Add(subject);
                break;
            case "topics":
                if (model.Id == 0)
                    db.Topics.Add(new Topic { Name = model.Name, SubjectId = model.SubjectId!.Value });
                else if (!await db.Topics.AsNoTracking().AnyAsync(x => x.Id == model.Id))
                    return NotFound();
                break;
            case "types":
                var type = model.Id == 0 ? new ResourceType() : await db.ResourceTypes.FindAsync(model.Id);
                if (type is null) return NotFound();
                type.Name = model.Name;
                if (model.Id == 0) db.ResourceTypes.Add(type);
                break;
            case "tags":
                var tag = model.Id == 0 ? new Tag() : await db.Tags.FindAsync(model.Id);
                if (tag is null) return NotFound();
                tag.Name = model.Name;
                if (model.Id == 0) db.Tags.Add(tag);
                break;
        }

        try
        {
            if (model.Kind == "topics" && model.Id > 0)
            {
                // SubjectId participates in the database alternate key. SQL updates permit an
                // unused topic to move while the composite resource FK blocks linked topics.
                foreach (var entry in db.ChangeTracker.Entries<Topic>().Where(x => x.Entity.Id == model.Id).ToList())
                    entry.State = EntityState.Detached;
                var affected = await db.Topics.Where(x => x.Id == model.Id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Name, model.Name)
                    .SetProperty(x => x.SubjectId, model.SubjectId!.Value));
                if (affected == 0) return NotFound();
            }
            else await db.SaveChangesAsync();
            TempData["Success"] = "Өзгерістер сақталды.";
            return RedirectToAction(nameof(Lookups), new { kind = model.Kind });
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            logger.LogWarning(ex, "Lookup save failed for {Kind} {Id}.", model.Kind, model.Id);
            ModelState.AddModelError("", "Жазба басқа деректерде қолданылған немесе атауы қайталанған болуы мүмкін. Бетті жаңартып, байланыстарын тексеріңіз.");
            await PopulateLookupAsync(model);
            return View(model);
        }
    }

    [HttpPost]
    public async Task<IActionResult> DeleteLookup(string kind, int id)
    {
        if (!IsKind(kind)) return NotFound();
        var referenced = kind switch
        {
            "subjects" => await db.Topics.AnyAsync(x => x.SubjectId == id) || await db.Resources.AnyAsync(x => x.SubjectId == id),
            "topics" => await db.Resources.AnyAsync(x => x.TopicId == id),
            "types" => await db.Resources.AnyAsync(x => x.ResourceTypeId == id),
            _ => await db.ResourceTags.AnyAsync(x => x.TagId == id)
        };
        if (referenced)
        {
            TempData["Error"] = "Бұл жазба материалдарда немесе тақырыптарда қолданылғандықтан жойылмайды. Алдымен байланыстарын өзгертіңіз.";
            return RedirectToAction(nameof(Lookups), new { kind });
        }

        object? entity = kind switch
        {
            "subjects" => await db.Subjects.FindAsync(id),
            "topics" => await db.Topics.FindAsync(id),
            "types" => await db.ResourceTypes.FindAsync(id),
            _ => await db.Tags.FindAsync(id)
        };
        if (entity is null) return NotFound();
        db.Remove(entity);
        try
        {
            await db.SaveChangesAsync();
            TempData["Success"] = "Жазба жойылды.";
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Lookup deletion blocked for {Kind} {Id}.", kind, id);
            TempData["Error"] = "Жазба басқа деректерде қолданылғандықтан жойылмады. Бетті жаңартып, байланыстарын тексеріңіз.";
        }
        return RedirectToAction(nameof(Lookups), new { kind });
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search, int page = 1)
    {
        const int pageSize = 20;
        search = search?.Trim();
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(search)) query = query.Where(x => x.FullName.Contains(search) || (x.Email != null && x.Email.Contains(search)));
        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);
        var users = await query.OrderBy(x => x.FullName).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new { x.Id, x.FullName, x.Email }).ToListAsync();
        var ids = users.Select(x => x.Id).ToArray();
        var roles = await (from ur in db.UserRoles join r in db.Roles on ur.RoleId equals r.Id where ids.Contains(ur.UserId) select new { ur.UserId, r.Name }).ToListAsync();
        return View(new UsersListViewModel
        {
            Search = search, Page = page, TotalCount = total, TotalPages = totalPages,
            Users = users.Select(x => new UserRoleViewModel { Id = x.Id, FullName = x.FullName, Email = x.Email ?? "", Roles = roles.Where(r => r.UserId == x.Id).Select(r => r.Name!).ToList() }).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> ChangeRole(ChangeRoleViewModel model)
    {
        if (!ModelState.IsValid || !UserAdministrationService.IsAllowedRole(model.Role))
        {
            TempData["Error"] = "Қолданушы мен рөлді дұрыс таңдаңыз.";
            return RedirectToAction(nameof(Users));
        }
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        try
        {
            var result = await userAdministration.ChangeRoleAsync(actorId, model.UserId, model.Role, HttpContext.RequestAborted);
            if (!result.Succeeded) TempData["Error"] = result.Error;
            else
            {
                if (actorId == model.UserId)
                {
                    await signInManager.SignOutAsync();
                    TempData["Success"] = "Рөліңіз өзгертілді. Жаңа құқықтармен қайта кіріңіз.";
                    return RedirectToAction("Login", "Account");
                }
                TempData["Success"] = "Қолданушы рөлі өзгертілді. Ол жүйеге қайта кіруі керек.";
            }
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            logger.LogWarning(ex, "Role update failed.");
            TempData["Error"] = "Рөлді өзгерту мүмкін болмады. Бетті жаңартып, қайталап көріңіз.";
        }
        return RedirectToAction(nameof(Users));
    }

    private async Task PopulateLookupAsync(LookupEditViewModel model)
    {
        model.Title = SingularTitle(model.Kind);
        if (model.Kind == "topics") model.Subjects = await db.Subjects.AsNoTracking().OrderBy(x => x.Name).Select(x => new SelectListItem(x.Name, x.Id.ToString())).ToListAsync();
    }

    private static bool IsKind(string? kind) => kind is "subjects" or "topics" or "types" or "tags";
    private static string PluralTitle(string kind) => kind switch { "subjects" => "Пәндер", "topics" => "Тақырыптар", "types" => "Материал түрлері", _ => "Тегтер" };
    private static string SingularTitle(string kind) => kind switch { "subjects" => "Пән", "topics" => "Тақырып", "types" => "Материал түрі", _ => "Тег" };
}
