using System.ComponentModel.DataAnnotations;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Controllers;

public class ResourcesController(ApplicationDbContext db, ResourceService service, IFileStorage storage, ILogger<ResourcesController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] CatalogQuery query, CancellationToken ct) => View(await service.CatalogAsync(query, User, ct));

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var resource = await db.Resources.AsNoTracking().Include(r => r.Subject).Include(r => r.Topic).Include(r => r.ResourceType)
            .Include(r => r.Owner).Include(r => r.ResourceTags).ThenInclude(rt => rt.Tag).Include(r => r.Analysis)
            .VisibleTo(User).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        var userId = ResourceAccess.UserId(User);
        db.ResourceViews.Add(new ResourceView { ResourceId = id, UserId = userId });
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return View(new ResourceDetailsViewModel
        {
            Resource = resource, CanEdit = ResourceAccess.CanManage(User, resource), FileExists = storage.Exists(resource.StoredFileName),
            IsFavorite = userId is not null && await db.Favorites.AnyAsync(f => f.UserId == userId && f.ResourceId == id, ct),
            ViewCount = await db.ResourceViews.CountAsync(v => v.ResourceId == id, ct),
            DownloadCount = await db.ResourceDownloads.CountAsync(v => v.ResourceId == id, ct)
        });
    }

    [HttpGet]
    public async Task<IActionResult> Preview(int id, CancellationToken ct)
    {
        var resource = await db.Resources.AsNoTracking().VisibleTo(User).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        if (!storage.Exists(resource.StoredFileName)) return MissingFile();
        SetFileHeaders();
        Response.Headers.ContentDisposition = "inline";
        return PhysicalFile(storage.GetPhysicalPath(resource.StoredFileName), "application/pdf", enableRangeProcessing: true);
    }

    [HttpPost]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var resource = await db.Resources.AsNoTracking().VisibleTo(User).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        if (!storage.Exists(resource.StoredFileName)) return MissingFile();
        db.ResourceDownloads.Add(new ResourceDownload { ResourceId = id, UserId = ResourceAccess.UserId(User) });
        await db.SaveChangesAsync(ct);
        SetFileHeaders();
        return PhysicalFile(storage.GetPhysicalPath(resource.StoredFileName), "application/pdf", resource.OriginalFileName, enableRangeProcessing: false);
    }

    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new ResourceEditViewModel();
        await service.FillOptionsAsync(model, ct);
        return View("Edit", model);
    }

    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpPost]
    public async Task<IActionResult> Create(ResourceEditViewModel model, CancellationToken ct)
    {
        model.Id = null;
        if (ModelState.IsValid)
        {
            try
            {
                var resource = await service.CreateAsync(model, User, ct);
                TempData["Success"] = "Материал сақталды.";
                return RedirectToAction(nameof(Details), new { id = resource.Id });
            }
            catch (Exception ex) when (ex is ValidationException or FileValidationException) { ModelState.AddModelError("", ex.Message); }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Resource creation failed.");
                ModelState.AddModelError("", "Материал сақталмады. Деректер мен файлды тексеріп, қайта көріңіз.");
            }
        }
        await service.FillOptionsAsync(model, ct);
        return View("Edit", model);
    }

    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var resource = await db.Resources.AsNoTracking().Include(r => r.ResourceTags).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        if (!ResourceAccess.CanManage(User, resource)) return Forbid();
        var model = new ResourceEditViewModel
        {
            Id = id, RowVersion = Convert.ToBase64String(resource.RowVersion), Title = resource.Title, Description = resource.Description, SubjectId = resource.SubjectId, TopicId = resource.TopicId,
            ResourceTypeId = resource.ResourceTypeId, TagIds = resource.ResourceTags.Select(t => t.TagId).ToList(),
            Status = resource.Status, AccessLevel = resource.AccessLevel, ExistingFileName = resource.OriginalFileName
        };
        await service.FillOptionsAsync(model, ct);
        return View(model);
    }

    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpPost]
    public async Task<IActionResult> Edit(int id, ResourceEditViewModel model, CancellationToken ct)
    {
        var resource = await db.Resources.Include(r => r.ResourceTags).Include(r => r.Analysis).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        if (!ResourceAccess.CanManage(User, resource)) return Forbid();
        model.Id = id; model.ExistingFileName = resource.OriginalFileName;
        if (ModelState.IsValid)
        {
            try
            {
                await service.UpdateAsync(resource, model, User, ct);
                TempData["Success"] = "Өзгерістер сақталды.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (DbUpdateConcurrencyException)
            {
                ModelState.AddModelError("", "Материалды басқа сұраныс өзгертті. Осы беттегі мәтінді көшіріп алып, бетті жаңартыңыз және өзгерістерді қайталаңыз.");
            }
            catch (Exception ex) when (ex is ValidationException or FileValidationException) { ModelState.AddModelError("", ex.Message); }
            catch (Exception ex) when (ex is DbUpdateException or IOException or UnauthorizedAccessException)
            {
                logger.LogError(ex, "Resource update failed.");
                ModelState.AddModelError("", "Өзгерістер сақталмады. Қайта көріңіз.");
            }
        }
        await service.FillOptionsAsync(model, ct);
        return View(model);
    }

    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpPost]
    public async Task<IActionResult> SetStatus(int id, ResourceStatus status, CancellationToken ct)
    {
        var resource = await db.Resources.SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        if (!ResourceAccess.CanManage(User, resource)) return Forbid();
        if (!Enum.IsDefined(status) || !ModelState.IsValid) return BadRequest();
        resource.Status = status; resource.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        TempData["Success"] = "Материал күйі жаңартылды.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private IActionResult MissingFile()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewData["StatusCode"] = 404;
        ViewData["Message"] = "Материалдың PDF файлы табылмады. Материал иесіне хабарласыңыз.";
        return View("~/Views/Home/StatusCodePage.cshtml");
    }
    private void SetFileHeaders()
    {
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'; frame-ancestors 'self'";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
}
