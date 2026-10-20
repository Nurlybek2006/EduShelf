using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.Services.Processing;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EduShelf.Controllers;

[Authorize(Roles = AppRoles.Teacher + "," + AppRoles.Admin)]
public sealed class ProcessingController(ApplicationDbContext db, IProcessingQueue queue, IOptions<AiOptions> options) : Controller
{
    [HttpPost]
    public async Task<IActionResult> Analyze(int id, CancellationToken cancellationToken)
    {
        var resource = await db.Resources.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (resource is null || !ResourceAccess.CanManage(User, resource)) return NotFound();
        await queue.EnqueueAsync(id, true, cancellationToken);
        TempData["Success"] = options.Value.IsConfigured ? "Құжатты талдау кезекке қосылды." : "AI қызметі бапталмаған. PDF мәтінін алу кезекке қосылды.";
        return RedirectToAction("Details", "Resources", new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Status(int id, CancellationToken cancellationToken)
    {
        var resource = await db.Resources.AsNoTracking().Include(x => x.TextExtraction).Include(x => x.ProcessingJob)
            .Include(x => x.Analysis)!.ThenInclude(x => x!.SuggestedSubject)
            .Include(x => x.Analysis)!.ThenInclude(x => x!.SuggestedTopic)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (resource is null || !ResourceAccess.CanManage(User, resource)) return NotFound();
        Response.Headers.CacheControl = "no-store";
        return Json(ProcessingPanelViewModel.From(resource, options.Value.IsConfigured));
    }

    [HttpPost]
    public async Task<IActionResult> AcceptSuggestion(int id, string? fileHash, int subjectId, int? topicId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var resource = await db.Resources.FromSqlInterpolated($"SELECT * FROM [Resources] WITH (UPDLOCK, HOLDLOCK) WHERE [Id]={id}")
            .Include(x => x.Analysis).SingleOrDefaultAsync(cancellationToken);
        if (resource is null || !ResourceAccess.CanManage(User, resource)) return NotFound();
        var analysis = resource.Analysis;
        if (analysis?.Status != AnalysisStatus.Completed || string.IsNullOrEmpty(fileHash) ||
            fileHash != resource.FileHash || analysis.AnalyzedFileHash != resource.FileHash ||
            analysis.SuggestedSubjectId != subjectId || analysis.SuggestedTopicId != topicId ||
            !await db.Subjects.AnyAsync(x => x.Id == subjectId, cancellationToken) ||
            (topicId.HasValue && !await db.Topics.AnyAsync(x => x.Id == topicId && x.SubjectId == subjectId, cancellationToken)))
        {
            TempData["Error"] = "Ұсыныс ескірген немесе таңдалған пән мен тақырып сәйкес келмейді. Бетті жаңартыңыз.";
            return RedirectToAction("Details", "Resources", new { id });
        }
        resource.SubjectId = subjectId;
        resource.TopicId = topicId;
        resource.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        TempData["Success"] = "AI ұсынған пән мен тақырып сіздің растауыңызбен сақталды.";
        return RedirectToAction("Details", "Resources", new { id });
    }
}
