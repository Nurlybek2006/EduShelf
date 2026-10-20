using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Services;

public sealed class AnalyticsService(ApplicationDbContext db, TimeProvider clock)
{
    public const int PopularLimit = 10;
    public const int StudentLimit = 100;
    public const int SubjectLimit = 200;

    public async Task<AnalyticsReportViewModel> BuildAsync(AnalyticsQuery query, ClaimsPrincipal user, CancellationToken ct = default)
    {
        if (user.Identity?.IsAuthenticated != true || (!user.IsInRole(AppRoles.Admin) && !user.IsInRole(AppRoles.Teacher)))
            throw new UnauthorizedAccessException();
        query.SetDefaults(clock.GetUtcNow());
        Validator.ValidateObject(query, new ValidationContext(query), true);
        var administrator = user.IsInRole(AppRoles.Admin);
        var userId = ResourceAccess.UserId(user);
        if (!administrator && string.IsNullOrEmpty(userId)) throw new UnauthorizedAccessException();
        var from = DateTime.SpecifyKind(query.From!.Value.ToDateTime(TimeOnly.MinValue).AddHours(-5), DateTimeKind.Utc);
        var until = DateTime.SpecifyKind(query.To!.Value.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-5), DateTimeKind.Utc);
        var resources = db.Resources.AsNoTracking().Where(r => administrator || r.OwnerId == userId);
        var ids = resources.Select(r => r.Id);
        var views = db.ResourceViews.AsNoTracking().Where(e => ids.Contains(e.ResourceId) && e.OccurredAtUtc >= from && e.OccurredAtUtc < until);
        var downloads = db.ResourceDownloads.AsNoTracking().Where(e => ids.Contains(e.ResourceId) && e.OccurredAtUtc >= from && e.OccurredAtUtc < until);
        var report = new AnalyticsReportViewModel
        {
            Query = query, IsAdministrator = administrator,
            ResourceCount = await resources.CountAsync(ct),
            PublishedCount = await resources.CountAsync(r => r.Status == ResourceStatus.Published, ct),
            ViewCount = await views.CountAsync(ct), DownloadCount = await downloads.CountAsync(ct)
        };

        // The counts, grouping, ordering and limits execute in SQL; no resource/event collection is loaded.
        var popularity = resources.Select(r => new PopularResourceRow
        {
            ResourceId = r.Id, Title = r.Title, Subject = r.Subject.Name,
            Views = views.Count(v => v.ResourceId == r.Id), Downloads = downloads.Count(d => d.ResourceId == r.Id)
        });
        report.MostViewed = await popularity.Where(r => r.Views > 0).OrderByDescending(r => r.Views).ThenBy(r => r.ResourceId).Take(PopularLimit).ToListAsync(ct);
        report.MostDownloaded = await popularity.Where(r => r.Downloads > 0).OrderByDescending(r => r.Downloads).ThenBy(r => r.ResourceId).Take(PopularLimit).ToListAsync(ct);
        var viewDays = await views.GroupBy(e => e.OccurredAtUtc.AddHours(5).Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var downloadDays = await downloads.GroupBy(e => e.OccurredAtUtc.AddHours(5).Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var byViewDay = viewDays.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        var byDownloadDay = downloadDays.ToDictionary(x => DateOnly.FromDateTime(x.Day), x => x.Count);
        for (var day = query.From.Value; day <= query.To.Value; day = day.AddDays(1))
            report.DailyActivity.Add(new DailyActivityRow { Day = day, Views = byViewDay.GetValueOrDefault(day), Downloads = byDownloadDay.GetValueOrDefault(day) });

        report.Subjects = await db.Subjects.AsNoTracking().OrderBy(s => s.Name).Select(s => new AnalyticsCountRow
        {
            Name = s.Name, Count = resources.Count(r => r.SubjectId == s.Id && r.Status == ResourceStatus.Published)
        }).Take(SubjectLimit).ToListAsync(ct);

        // Only persisted Student-role memberships count. Anonymous events stay in totals but cannot become a student.
        var studentIds = from membership in db.UserRoles
                         join role in db.Roles on membership.RoleId equals role.Id
                         where role.Name == AppRoles.Student
                         select membership.UserId;
        var studentViews = views.Where(e => e.UserId != null && studentIds.Contains(e.UserId))
            .Select(e => new { UserId = e.UserId!, e.User!.FullName, e.ResourceId, e.OccurredAtUtc, IsView = true });
        var studentDownloads = downloads.Where(e => e.UserId != null && studentIds.Contains(e.UserId))
            .Select(e => new { UserId = e.UserId!, e.User!.FullName, e.ResourceId, e.OccurredAtUtc, IsView = false });
        report.StudentActivity = await studentViews.Concat(studentDownloads).GroupBy(e => new { e.UserId, e.FullName })
            .Select(g => new StudentActivityRow
            {
                UserId = g.Key.UserId, FullName = g.Key.FullName,
                Views = g.Count(e => e.IsView), Downloads = g.Count(e => !e.IsView),
                UniqueViewedResources = g.Where(e => e.IsView).Select(e => e.ResourceId).Distinct().Count(),
                LastActivityUtc = g.Max(e => e.OccurredAtUtc)
            }).OrderByDescending(e => e.LastActivityUtc).ThenBy(e => e.UserId).Take(StudentLimit).ToListAsync(ct);

        var currentAnalyses = resources.Where(r => r.Analysis != null && r.Analysis.Status == AnalysisStatus.Completed
            && r.Analysis.AnalyzedFileHash != null && r.Analysis.AnalyzedFileHash == r.FileHash);
        report.AnalyzedCount = await currentAnalyses.CountAsync(ct);
        var statuses = await resources.GroupBy(r => r.Analysis == null || (r.Analysis.Status == AnalysisStatus.Completed
                && (r.Analysis.AnalyzedFileHash == null || r.Analysis.AnalyzedFileHash != r.FileHash))
                ? AnalysisStatus.NotAnalyzed : r.Analysis.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        report.AnalysisStatuses = Enum.GetValues<AnalysisStatus>().Select(status => new AnalyticsCountRow
        {
            Name = StatusLabel(status), Count = statuses.Where(s => s.Status == status).Select(s => s.Count).SingleOrDefault()
        }).ToList();
        report.Difficulties = await currentAnalyses.GroupBy(r => r.Analysis!.Difficulty ?? "Көрсетілмеген")
            .Select(g => new AnalyticsCountRow { Name = g.Key, Count = g.Count() }).OrderByDescending(r => r.Count).ThenBy(r => r.Name).Take(10).ToListAsync(ct);
        report.DetectedTopics = await currentAnalyses.GroupBy(r => r.Analysis!.DetectedTopic ?? "Көрсетілмеген")
            .Select(g => new AnalyticsCountRow { Name = g.Key, Count = g.Count() }).OrderByDescending(r => r.Count).ThenBy(r => r.Name).Take(20).ToListAsync(ct);
        return report;
    }

    public static string StatusLabel(AnalysisStatus status) => status.ToString() switch
    {
        "NotAnalyzed" => "Талданбаған", "Pending" => "Кезекте", "Processing" => "Өңделуде",
        "Completed" => "Дайын", "Failed" => "Қате", "NotConfigured" => "AI қызметі бапталмаған", _ => "Белгісіз"
    };
}

public static class AnalyticsCsv
{
    public static byte[] Export(AnalyticsReportViewModel report)
    {
        var csv = new StringBuilder();
        void Row(params object?[] fields) => csv.AppendJoin(',', fields.Select(Escape)).Append("\r\n");
        Row("EduShelf аналитикасы", report.IsAdministrator ? "Бүкіл жүйе" : "Менің материалдарым");
        Row("Басталу күні", report.Query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Аяқталу күні", report.Query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "Уақыт белдеуі", "UTC+05:00");
        Row("Көрсеткіш", "Саны");
        Row("Материалдар (қазіргі күйі)", report.ResourceCount); Row("Жария материалдар (қазіргі күйі)", report.PublishedCount);
        Row("Аралықтағы қаралым", report.ViewCount); Row("Аралықтағы жүктеу", report.DownloadCount);
        Row("Талданған", report.AnalyzedCount); Row("Талданбаған", report.UnanalyzedCount);
        void Popular(string heading, IEnumerable<PopularResourceRow> rows)
        {
            Row(); Row(heading); Row("Материал ID", "Атауы", "Пән", "Қаралым", "Жүктеу");
            foreach (var item in rows) Row(item.ResourceId, item.Title, item.Subject, item.Views, item.Downloads);
        }
        Popular("Ең көп қаралған (10)", report.MostViewed); Popular("Ең көп жүктелген (10)", report.MostDownloaded);
        Row(); Row("Күндік белсенділік"); Row("Күн (UTC+05:00)", "Қаралым", "Жүктеу");
        foreach (var item in report.DailyActivity) Row(item.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), item.Views, item.Downloads);
        void Counts(string heading, IEnumerable<AnalyticsCountRow> rows)
        {
            Row(); Row(heading); Row("Атауы", "Саны"); foreach (var item in rows) Row(item.Name, item.Count);
        }
        Counts("Пәндер бойынша жария материалдар (алғашқы 200 пән)", report.Subjects);
        Row(); Row("Студент белсенділігі (соңғы белсенділік бойынша 100 студент)");
        Row("Студент ID", "Аты-жөні", "Қаралым", "Жүктеу", "Бірегей қаралған материал", "Соңғы белсенділік (UTC+05:00)");
        foreach (var item in report.StudentActivity)
            Row(item.UserId, item.FullName, item.Views, item.Downloads, item.UniqueViewedResources, item.LastActivityUtc.AddHours(5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Counts("AI талдау статустары (қазіргі күйі)", report.AnalysisStatuses);
        Counts("AI қиындық деңгейлері", report.Difficulties); Counts("AI анықтаған тақырыптар (алғашқы 20)", report.DetectedTopics);
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    private static string Escape(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var first = text.AsSpan().TrimStart();
        if ((!first.IsEmpty && first[0] is '=' or '+' or '-' or '@') || (text.Length > 0 && text[0] is '\t' or '\r' or '\n'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
