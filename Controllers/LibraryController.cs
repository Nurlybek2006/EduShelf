using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Controllers;

[Authorize]
public class LibraryController(ApplicationDbContext db, ResourceService resources, LibraryService library) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Favorites(CancellationToken ct)
    {
        var userId = ResourceAccess.UserId(User);
        var query = db.Resources.AsNoTracking().VisibleTo(User).Where(r => db.Favorites.Any(f => f.ResourceId == r.Id && f.UserId == userId)).OrderByDescending(r => r.CreatedAtUtc);
        return View(new PersonalLibraryViewModel { Title = "Таңдаулы материалдар", Description = "Кейін оқу үшін сақтаған білім қорыңыз.", Items = await resources.CardListAsync(query, User, ct) });
    }
    [HttpPost]
    public async Task<IActionResult> ToggleFavorite(int id, string? returnUrl, CancellationToken ct)
    {
        try
        {
            var added = await library.ToggleFavoriteAsync(id, User, ct);
            TempData["Success"] = added ? "Материал таңдаулыға қосылды." : "Материал таңдаулыдан алынды.";
        }
        catch (KeyNotFoundException) { return NotFound(); }
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction("Details", "Resources", new { id });
    }
    [HttpPost]
    public async Task<IActionResult> SaveBookmark(int id, int pageNumber, CancellationToken ct)
    {
        var resource = await db.Resources.AsNoTracking().VisibleTo(User).Include(r => r.TextExtraction).SingleOrDefaultAsync(r => r.Id == id, ct);
        if (resource is null) return NotFound();
        var maximum = resource.TextExtraction?.ExtractedFileHash == resource.FileHash && resource.TextExtraction.TotalPages > 0 ? resource.TextExtraction.TotalPages : 10000;
        if (!ModelState.IsValid || pageNumber < 1 || pageNumber > maximum) return BadRequest();
        var userId = ResourceAccess.UserId(User)!;
        var bookmark = await db.PageBookmarks.FindAsync([userId, id], ct);
        if (bookmark is null) { bookmark = new PageBookmark { UserId = userId, ResourceId = id }; db.PageBookmarks.Add(bookmark); }
        bookmark.PageNumber = pageNumber; bookmark.FileHash = resource.FileHash; bookmark.UpdatedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(ct); TempData["Success"] = $"{pageNumber}-бет жеке бетбелгіге сақталды."; }
        catch (DbUpdateException) { TempData["Error"] = "Бетбелгі сақталмады. Бетті жаңартып, қайта көріңіз."; }
        return RedirectToAction("Details", "Resources", new { id });
    }
    [HttpGet]
    public async Task<IActionResult> History(CancellationToken ct)
    {
        var userId = ResourceAccess.UserId(User);
        var visibleIds = db.Resources.VisibleTo(User).Select(r => r.Id);
        var views = db.ResourceViews.AsNoTracking().Where(v => v.UserId == userId && visibleIds.Contains(v.ResourceId))
            .Select(v => new HistoryItemViewModel { ResourceId = v.ResourceId, Title = v.Resource.Title, Action = "Қаралды", OccurredAtUtc = v.OccurredAtUtc });
        var downloads = db.ResourceDownloads.AsNoTracking().Where(v => v.UserId == userId && visibleIds.Contains(v.ResourceId))
            .Select(v => new HistoryItemViewModel { ResourceId = v.ResourceId, Title = v.Resource.Title, Action = "Жүктелді", OccurredAtUtc = v.OccurredAtUtc });
        return View(new HistoryViewModel { Items = await views.Concat(downloads).OrderByDescending(x => x.OccurredAtUtc).Take(200).ToListAsync(ct) });
    }
    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpGet]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var query = OwnedResources().OrderByDescending(r => r.UpdatedAtUtc);
        return View(new PersonalLibraryViewModel { Title = User.IsInRole(AppRoles.Admin) ? "Барлық материалдар" : "Менің материалдарым", Description = "Материалдарды өңдеңіз, жариялаңыз және архивтеңіз.", Items = await resources.CardListAsync(query, User, ct) });
    }
    [Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin), HttpGet]
    public IActionResult Analytics(CancellationToken ct) => RedirectToAction("Index", "Analytics");
    private IQueryable<Resource> OwnedResources()
    {
        var userId = ResourceAccess.UserId(User);
        return User.IsInRole(AppRoles.Admin) ? db.Resources.AsNoTracking() : db.Resources.AsNoTracking().Where(r => r.OwnerId == userId);
    }
}
