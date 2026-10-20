using System.Diagnostics;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Controllers;

public class HomeController(ApplicationDbContext db, ResourceService resources) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var visible = db.Resources.AsNoTracking().VisibleTo(User, catalogOnly: true);
        return View(new HomeViewModel
        {
            ResourceCount = await visible.CountAsync(ct), SubjectCount = await db.Subjects.CountAsync(ct),
            DownloadCount = await visible.SelectMany(r => r.Downloads).CountAsync(ct),
            Latest = await resources.CardListAsync(visible.OrderByDescending(r => r.CreatedAtUtc).ThenByDescending(r => r.Id).Take(6), User, ct),
            Popular = await resources.CardListAsync(visible.OrderByDescending(r => r.Views.Count).ThenByDescending(r => r.Id).Take(3), User, ct),
            Subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Take(8).ToListAsync(ct)
        });
    }
    public IActionResult Privacy() => View();
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult StatusCodePage(int code)
    {
        Response.StatusCode = code;
        ViewData["StatusCode"] = code;
        return View();
    }
}
