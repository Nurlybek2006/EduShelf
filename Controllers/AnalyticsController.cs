using System.ComponentModel.DataAnnotations;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduShelf.Controllers;

[Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AnalyticsController(AnalyticsService analytics, TimeProvider clock) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] AnalyticsQuery query, CancellationToken ct)
    {
        query.SetDefaults(clock.GetUtcNow());
        ValidateRange(query);
        if (!ModelState.IsValid)
            return View(new AnalyticsReportViewModel { Query = query, IsValid = false, IsAdministrator = User.IsInRole(AppRoles.Admin) });
        return View(await analytics.BuildAsync(query, User, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Export([FromQuery] AnalyticsQuery query, CancellationToken ct)
    {
        query.SetDefaults(clock.GetUtcNow());
        ValidateRange(query);
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return View("Index", new AnalyticsReportViewModel { Query = query, IsValid = false, IsAdministrator = User.IsInRole(AppRoles.Admin) });
        }
        var report = await analytics.BuildAsync(query, User, ct);
        return File(AnalyticsCsv.Export(report), "text/csv; charset=utf-8", $"edushelf-{query.From:yyyy-MM-dd}-{query.To:yyyy-MM-dd}.csv");
    }

    private void ValidateRange(AnalyticsQuery query)
    {
        // Defaults are filled after model binding, so validate the effective range too.
        foreach (var error in query.Validate(new ValidationContext(query)))
            foreach (var member in error.MemberNames.DefaultIfEmpty(""))
                if (!ModelState.TryGetValue(member, out var entry) || entry.Errors.All(e => e.ErrorMessage != error.ErrorMessage))
                    ModelState.AddModelError(member, error.ErrorMessage!);
        foreach (var field in new[] { nameof(AnalyticsQuery.From), nameof(AnalyticsQuery.To) })
            if (ModelState.TryGetValue(field, out var entry) && entry.Errors.Any(e => e.Exception != null))
            {
                entry.Errors.Clear();
                entry.Errors.Add("Күнді дұрыс пішімде енгізіңіз (жыл-ай-күн).");
            }
    }
}
