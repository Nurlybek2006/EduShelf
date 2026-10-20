using System.ComponentModel.DataAnnotations;
using System.Text;
using EduShelf.Controllers;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Tests;

public sealed class AnalyticsTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    private static readonly TimeProvider Clock = new FixedClock(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
    private static AnalyticsQuery Period() => new() { From = new DateOnly(2026, 10, 19), To = new DateOnly(2026, 10, 20) };

    [SqlServerFact]
    public async Task Teacher_reports_and_csv_include_only_owned_resource_events_and_persisted_students()
    {
        var own = await fixture.CreateCaseAsync();
        var other = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        await AddStudentRoleAsync(db, own.StudentId);
        db.ResourceViews.AddRange(
            new ResourceView { ResourceId = own.ResourceId, UserId = own.StudentId, OccurredAtUtc = new DateTime(2026, 10, 20, 3, 0, 0) },
            new ResourceView { ResourceId = own.ResourceId, UserId = null, OccurredAtUtc = new DateTime(2026, 10, 20, 4, 0, 0) },
            new ResourceView { ResourceId = own.ResourceId, UserId = own.TeacherId, OccurredAtUtc = new DateTime(2026, 10, 20, 5, 0, 0) },
            new ResourceView { ResourceId = other.ResourceId, UserId = own.StudentId, OccurredAtUtc = new DateTime(2026, 10, 20, 6, 0, 0) });
        db.ResourceDownloads.AddRange(
            new ResourceDownload { ResourceId = own.ResourceId, UserId = own.StudentId, OccurredAtUtc = new DateTime(2026, 10, 20, 7, 0, 0) },
            new ResourceDownload { ResourceId = other.ResourceId, UserId = own.StudentId, OccurredAtUtc = new DateTime(2026, 10, 20, 8, 0, 0) });
        await db.SaveChangesAsync();
        var service = new AnalyticsService(db, Clock);
        var report = await service.BuildAsync(Period(), own.Teacher);
        Assert.Equal(1, report.ResourceCount);
        Assert.Equal(3, report.ViewCount);
        Assert.Equal(1, report.DownloadCount);
        Assert.Equal(own.ResourceId, Assert.Single(report.MostViewed).ResourceId);
        Assert.Equal(own.ResourceId, Assert.Single(report.MostDownloaded).ResourceId);
        var student = Assert.Single(report.StudentActivity);
        Assert.Equal(own.StudentId, student.UserId);
        Assert.Equal(1, student.Views);
        Assert.Equal(1, student.Downloads);
        Assert.Equal(1, student.UniqueViewedResources);
        Assert.Equal(new DateTime(2026, 10, 20, 7, 0, 0), student.LastActivityUtc);
        var csv = Encoding.UTF8.GetString(AnalyticsCsv.Export(report));
        Assert.Contains("Public " + own.Suffix, csv);
        Assert.DoesNotContain("Public " + other.Suffix, csv);
        var administrator = await service.BuildAsync(Period(), ResourceAccessTests.Principal("admin", AppRoles.Admin));
        Assert.Contains(administrator.MostViewed, r => r.ResourceId == other.ResourceId);
        var adminStudent = Assert.Single(administrator.StudentActivity, r => r.UserId == own.StudentId);
        Assert.Equal(2, adminStudent.Views);
        Assert.Equal(2, adminStudent.Downloads);
        Assert.Equal(2, adminStudent.UniqueViewedResources);
    }

    [SqlServerFact]
    public async Task Download_only_student_has_no_unique_viewed_materials_and_repeated_views_count_once()
    {
        var data = await fixture.CreateCaseAsync();
        var downloadOnly = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        await AddStudentRoleAsync(db, data.StudentId);
        await AddStudentRoleAsync(db, downloadOnly.StudentId);
        db.ResourceViews.AddRange(Enumerable.Range(0, 3).Select(i => new ResourceView
        {
            ResourceId = data.ResourceId, UserId = data.StudentId, OccurredAtUtc = new DateTime(2026, 10, 20, 9, i, 0)
        }));
        db.ResourceDownloads.Add(new ResourceDownload
        {
            ResourceId = data.ResourceId, UserId = downloadOnly.StudentId, OccurredAtUtc = new DateTime(2026, 10, 20, 10, 0, 0)
        });
        await db.SaveChangesAsync();
        var report = await new AnalyticsService(db, Clock).BuildAsync(Period(), data.Teacher);
        var viewed = Assert.Single(report.StudentActivity, s => s.UserId == data.StudentId);
        Assert.Equal(3, viewed.Views);
        Assert.Equal(1, viewed.UniqueViewedResources);
        var downloaded = Assert.Single(report.StudentActivity, s => s.UserId == downloadOnly.StudentId);
        Assert.Equal(0, downloaded.Views);
        Assert.Equal(1, downloaded.Downloads);
        Assert.Equal(0, downloaded.UniqueViewedResources);
    }

    [SqlServerFact]
    public async Task Local_date_boundaries_are_inclusive_and_eventless_days_are_zero_filled()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        foreach (var occurred in new[]
        {
            new DateTime(2026, 10, 19, 18, 59, 59), new DateTime(2026, 10, 19, 19, 0, 0),
            new DateTime(2026, 10, 20, 18, 59, 59), new DateTime(2026, 10, 20, 19, 0, 0)
        }) db.ResourceViews.Add(new ResourceView { ResourceId = data.ResourceId, OccurredAtUtc = occurred });
        db.ResourceDownloads.Add(new ResourceDownload { ResourceId = data.ResourceId, OccurredAtUtc = new DateTime(2026, 10, 19, 20, 0, 0) });
        await db.SaveChangesAsync();
        var query = new AnalyticsQuery { From = new DateOnly(2026, 10, 20), To = new DateOnly(2026, 10, 22) };
        var report = await new AnalyticsService(db, Clock).BuildAsync(query, data.Teacher);
        // The event at 19:00 UTC belongs to the next local day.
        Assert.Equal(3, report.ViewCount);
        Assert.Equal(new[] { 2, 1, 0 }, report.DailyActivity.Select(x => x.Views));
        Assert.Equal(new[] { 1, 0, 0 }, report.DailyActivity.Select(x => x.Downloads));
        var singleDay = await new AnalyticsService(db, Clock).BuildAsync(new AnalyticsQuery { From = new DateOnly(2026, 10, 20), To = new DateOnly(2026, 10, 20) }, data.Teacher);
        Assert.Equal(2, singleDay.ViewCount);
        Assert.Equal(1, singleDay.DownloadCount);
    }

    [SqlServerFact]
    public async Task AI_breakdowns_ignore_completed_results_from_an_old_file_hash()
    {
        var current = await fixture.CreateCaseAsync();
        var stale = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var resources = await db.Resources.Include(r => r.Analysis).Where(r => r.Id == current.ResourceId || r.Id == stale.ResourceId).ToListAsync();
        foreach (var resource in resources)
        {
            resource.OwnerId = current.TeacherId;
            resource.Analysis!.Status = AnalysisStatus.Completed;
            resource.Analysis.Difficulty = resource.Id == current.ResourceId ? "Орта" : "Жоғары";
            resource.Analysis.DetectedTopic = resource.Id == current.ResourceId ? "Қазақша тақырып" : "Ескі нәтиже";
            resource.Analysis.AnalyzedFileHash = resource.Id == current.ResourceId ? resource.FileHash : new string('f', 64);
        }
        await db.SaveChangesAsync();
        var report = await new AnalyticsService(db, Clock).BuildAsync(Period(), current.Teacher);
        Assert.Equal(2, report.ResourceCount);
        Assert.Equal(1, report.AnalyzedCount);
        Assert.Equal(1, report.UnanalyzedCount);
        Assert.Equal("Орта", Assert.Single(report.Difficulties).Name);
        Assert.Equal("Қазақша тақырып", Assert.Single(report.DetectedTopics).Name);
        Assert.Equal(1, report.AnalysisStatuses.Single(x => x.Name == "Талданбаған").Count);
        Assert.Equal(1, report.AnalysisStatuses.Single(x => x.Name == "Дайын").Count);
    }

    [SqlServerFact]
    public async Task Empty_reports_remain_empty_and_service_rejects_student_role()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var service = new AnalyticsService(db, Clock);
        var empty = await service.BuildAsync(Period(), ResourceAccessTests.Principal("no-owned-resources", AppRoles.Teacher));
        Assert.Equal(0, empty.ResourceCount);
        Assert.Equal(0, empty.ViewCount);
        Assert.Empty(empty.MostViewed);
        Assert.Empty(empty.StudentActivity);
        Assert.All(empty.DailyActivity, d => { Assert.Equal(0, d.Views); Assert.Equal(0, d.Downloads); });
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.BuildAsync(Period(), data.Student));
    }

    [Fact]
    public void Defaults_use_the_Qyzylorda_local_day_and_range_validation_limits_work()
    {
        var query = new AnalyticsQuery();
        query.SetDefaults(new DateTimeOffset(2026, 10, 19, 20, 0, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 20), query.To);
        Assert.Equal(new DateOnly(2026, 9, 21), query.From);
        query.From = new DateOnly(2026, 10, 21);
        Assert.NotEmpty(query.Validate(new ValidationContext(query)));
        query.From = new DateOnly(2025, 10, 19);
        Assert.NotEmpty(query.Validate(new ValidationContext(query)));
        query.From = new DateOnly(2025, 10, 20);
        Assert.Empty(query.Validate(new ValidationContext(query))); // Inclusive 366 days.
    }

    [Fact]
    public void Both_report_and_export_have_server_role_authorization()
    {
        var authorization = Assert.Single(typeof(AnalyticsController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(AppRoles.Teacher + "," + AppRoles.Admin, authorization.Roles);
        Assert.NotNull(typeof(AnalyticsController).GetMethod(nameof(AnalyticsController.Index)));
        Assert.NotNull(typeof(AnalyticsController).GetMethod(nameof(AnalyticsController.Export)));
    }

    [Theory]
    [InlineData("=SUM(1,2)")]
    [InlineData("+cmd")]
    [InlineData("-cmd")]
    [InlineData("@SUM(A1)")]
    [InlineData("  =SUM(1,2)")]
    [InlineData("\t=SUM(1,2)")]
    [InlineData("\r\n@cmd")]
    [InlineData("\tplain text")]
    public void Csv_neutralizes_formulas_even_after_leading_whitespace(string title)
    {
        var report = new AnalyticsReportViewModel { MostViewed = [new PopularResourceRow { Title = title }] };
        var csv = Encoding.UTF8.GetString(AnalyticsCsv.Export(report));
        Assert.Contains("\"'" + title.Replace("\"", "\"\"") + "\"", csv);
    }

    [Fact]
    public void Csv_preserves_Unicode_BOM_and_quotes_newlines_commas_and_double_quotes()
    {
        var report = new AnalyticsReportViewModel
        {
            MostViewed = [new PopularResourceRow { Title = "Қазақша, \"Оқу\"\r\nРусский English" }]
        };
        var bytes = AnalyticsCsv.Export(report);
        Assert.Equal(new byte[] { 0xef, 0xbb, 0xbf }, bytes.Take(3));
        var csv = Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("\"Қазақша, \"\"Оқу\"\"\r\nРусский English\"", csv);
        Assert.Contains("\r\n", csv);
    }

    private static async Task AddStudentRoleAsync(EduShelf.Data.ApplicationDbContext db, string userId)
    {
        var role = await db.Roles.SingleOrDefaultAsync(r => r.Name == AppRoles.Student);
        if (role is null)
        {
            role = new IdentityRole(AppRoles.Student) { NormalizedName = "STUDENT" };
            db.Roles.Add(role);
            await db.SaveChangesAsync();
        }
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = role.Id });
        await db.SaveChangesAsync();
    }

    private sealed class FixedClock(DateTimeOffset utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utc;
    }
}
