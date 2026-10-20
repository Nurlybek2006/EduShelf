using System.ComponentModel.DataAnnotations;

namespace EduShelf.ViewModels;

public sealed class AnalyticsQuery : IValidatableObject
{
    [Display(Name = "Басталу күні"), DataType(DataType.Date)]
    public DateOnly? From { get; set; }
    [Display(Name = "Аяқталу күні"), DataType(DataType.Date)]
    public DateOnly? To { get; set; }

    public void SetDefaults(DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(5)).DateTime);
        From ??= today.AddDays(-29);
        To ??= today;
    }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (From is null || To is null) yield break;
        if (From > To)
            yield return new ValidationResult("Басталу күні аяқталу күнінен кейін болмауы керек.", [nameof(From), nameof(To)]);
        else if (To.Value.DayNumber - From.Value.DayNumber >= 366)
            yield return new ValidationResult("Есеп аралығы 366 күннен аспауы керек.", [nameof(From), nameof(To)]);
        if (From <= DateOnly.MinValue || To >= DateOnly.MaxValue)
            yield return new ValidationResult("Есеп үшін жарамды күндерді таңдаңыз.", [nameof(From), nameof(To)]);
    }
}

public sealed class AnalyticsReportViewModel
{
    public AnalyticsQuery Query { get; set; } = new();
    public bool IsAdministrator { get; set; }
    public bool IsValid { get; set; } = true;
    public int ResourceCount { get; set; }
    public int PublishedCount { get; set; }
    public int ViewCount { get; set; }
    public int DownloadCount { get; set; }
    public int AnalyzedCount { get; set; }
    public int UnanalyzedCount => ResourceCount - AnalyzedCount;
    public List<PopularResourceRow> MostViewed { get; set; } = [];
    public List<PopularResourceRow> MostDownloaded { get; set; } = [];
    public List<DailyActivityRow> DailyActivity { get; set; } = [];
    public List<AnalyticsCountRow> Subjects { get; set; } = [];
    public List<StudentActivityRow> StudentActivity { get; set; } = [];
    public List<AnalyticsCountRow> AnalysisStatuses { get; set; } = [];
    public List<AnalyticsCountRow> Difficulties { get; set; } = [];
    public List<AnalyticsCountRow> DetectedTopics { get; set; } = [];
}

public sealed class PopularResourceRow
{
    public int ResourceId { get; set; }
    public string Title { get; set; } = "";
    public string Subject { get; set; } = "";
    public int Views { get; set; }
    public int Downloads { get; set; }
}

public sealed class DailyActivityRow
{
    public DateOnly Day { get; set; }
    public int Views { get; set; }
    public int Downloads { get; set; }
}

public sealed class AnalyticsCountRow
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
}

public sealed class StudentActivityRow
{
    public string FullName { get; set; } = "";
    public string UserId { get; set; } = "";
    public int Views { get; set; }
    public int Downloads { get; set; }
    public int UniqueViewedResources { get; set; }
    public DateTime LastActivityUtc { get; set; }
}
