using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.Services.Processing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduShelf.Tests;

public sealed class PhaseTwoHttpTests : IAsyncLifetime
{
    private readonly SqlServerFixture database = new();
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "EduShelfPhaseTwoHttp", Guid.NewGuid().ToString("N"));
    private readonly string password = "Aa9!" + Guid.NewGuid().ToString("N");
    private PhaseTwoFactory? factory;
    private string TeacherEmail => $"teacher-{suffix}@example.test";
    private string StudentEmail => $"student-{suffix}@example.test";
    private string AdminEmail => $"admin-{suffix}@example.test";
    private int publicId, privateId;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EDUSHELF_TEST_CONNECTION"))) return;
        await database.InitializeAsync();
        await using var isolated = database.Open();
        var connection = isolated.Database.GetConnectionString()!;
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection, ["Storage:RootPath"] = temporaryRoot,
            ["Seed:DemoEnabled"] = "true", ["Seed:AdminEmail"] = AdminEmail, ["Seed:AdminPassword"] = password,
            ["Seed:TeacherEmail"] = TeacherEmail, ["Seed:TeacherPassword"] = password,
            ["Seed:StudentEmail"] = StudentEmail, ["Seed:StudentPassword"] = password,
            ["Ai:ApiKey"] = "", ["Worker:Enabled"] = "false", ["Search:Mode"] = "Like",
            ["Bootstrap:AdminEmail"] = "", ["Bootstrap:AdminPassword"] = ""
        };
        factory = new PhaseTwoFactory(connection, temporaryRoot, settings);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var hosted = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(isolated.Database.GetDbConnection().Database, hosted.Database.GetDbConnection().Database);
            Assert.StartsWith("EduShelf_Tests_", hosted.Database.GetDbConnection().Database);
            Assert.False(scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value.IsConfigured);
        }
        await DemoSeeder.SeedAsync(Factory.Services, Factory.Services.GetRequiredService<IWebHostEnvironment>(), Factory.Services.GetRequiredService<IConfiguration>());
        await using var db = database.Open();
        publicId = await db.Resources.Where(r => r.AccessLevel == AccessLevel.Public).Select(r => r.Id).FirstAsync();
        privateId = await db.Resources.Where(r => r.AccessLevel == AccessLevel.Authenticated).Select(r => r.Id).SingleAsync();
    }

    [SqlServerFact]
    public async Task AI_endpoints_enforce_owner_role_antiforgery_and_missing_key_queue_is_durable_and_unique()
    {
        using var guest = Client();
        using var anonymousStatus = await guest.GetAsync($"/Processing/Status/{publicId}");
        Redirect(anonymousStatus, "/Account/Login");
        using var student = Client();
        await LoginAsync(student, StudentEmail);
        using var studentStatus = await student.GetAsync($"/Processing/Status/{publicId}");
        Redirect(studentStatus, "/Account/AccessDenied");
        using var studentAnalyze = await PostAsync(student, "/Processing/Analyze", "/Library/Favorites", new() { ["id"] = publicId.ToString() });
        Redirect(studentAnalyze, "/Account/AccessDenied");

        var other = await CreateUserAsync(AppRoles.Teacher);
        using var otherTeacher = Client();
        await LoginAsync(otherTeacher, other.Email!);
        using var forbiddenStatus = await otherTeacher.GetAsync($"/Processing/Status/{publicId}");
        Assert.Equal(HttpStatusCode.NotFound, forbiddenStatus.StatusCode);
        using var forbiddenAnalyze = await PostAsync(otherTeacher, "/Processing/Analyze", "/Resources/Create", new() { ["id"] = publicId.ToString() });
        Assert.Equal(HttpStatusCode.NotFound, forbiddenAnalyze.StatusCode);

        using var teacher = Client();
        await LoginAsync(teacher, TeacherEmail);
        using var noToken = await teacher.PostAsync("/Processing/Analyze", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = publicId.ToString() }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var first = await PostAsync(teacher, "/Processing/Analyze", "/Resources/Create", new() { ["id"] = publicId.ToString() });
        using var duplicate = await PostAsync(teacher, "/Processing/Analyze", "/Resources/Create", new() { ["id"] = publicId.ToString() });
        Redirect(first, $"/Resources/Details/{publicId}");
        Redirect(duplicate, $"/Resources/Details/{publicId}");
        await using (var verify = database.Open())
        {
            var job = Assert.Single(await verify.ProcessingJobs.Where(x => x.ResourceId == publicId).ToListAsync());
            Assert.Equal(JobStatus.Pending, job.Status);
            Assert.True(job.AnalyzeRequested);
            Assert.Equal(AnalysisStatus.NotConfigured, (await verify.ResourceAnalyses.SingleAsync(x => x.ResourceId == publicId)).Status);
        }
        using var status = await teacher.GetAsync($"/Processing/Status/{publicId}");
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var payload = JsonDocument.Parse(await status.Content.ReadAsStringAsync());
        Assert.Equal("NotConfigured", payload.RootElement.GetProperty("status").GetString());
        Assert.False(payload.RootElement.TryGetProperty("extractedText", out _));
        await using var counts = database.Open();
        Assert.Equal(0, await counts.ResourceViews.CountAsync(x => x.ResourceId == publicId));
        Assert.Equal(0, await counts.ResourceDownloads.CountAsync(x => x.ResourceId == publicId));
    }

    [SqlServerFact]
    public async Task AI_output_is_encoded_viewer_has_no_status_endpoint_and_private_recommendation_is_hidden()
    {
        const string hostile = "<script>alert('analysis-xss')</script>";
        await using (var db = database.Open())
        {
            var resource = await db.Resources.Include(x => x.Analysis).SingleAsync(x => x.Id == publicId);
            resource.Analysis ??= new ResourceAnalysis();
            resource.Analysis.Status = AnalysisStatus.Completed;
            resource.Analysis.AnalyzedFileHash = resource.FileHash;
            resource.Analysis.DetectedTopic = hostile;
            resource.Analysis.Summary = hostile;
            resource.Analysis.Difficulty = "Орта";
            resource.Analysis.Keywords = JsonSerializer.Serialize(new[] { hostile, "сөз", "тест", "кітап", "білім" });
            resource.Analysis.IsPartial = true;
            resource.Analysis.SourceCharacters = 1000;
            resource.Analysis.AnalyzedCharacters = 500;
            (await db.Resources.SingleAsync(x => x.Id == privateId)).Title = "PRIVATE-RECOMMENDATION-" + suffix;
            await db.SaveChangesAsync();
        }
        using var guest = Client();
        var html = await HtmlAsync(guest, $"/Resources/Details/{publicId}");
        Assert.DoesNotContain(hostile, html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("Құжаттың бір бөлігі талданды", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain($"/Processing/Status/{publicId}", html);
        Assert.DoesNotContain("PRIVATE-RECOMMENDATION-" + suffix, html);
        Assert.Contains("data-copy-material-link", html);
    }

    [SqlServerFact]
    public async Task Suggested_classification_requires_owner_explicit_confirmation_and_current_hash()
    {
        int subjectId, topicId;
        string fileHash;
        await using (var db = database.Open())
        {
            var resource = await db.Resources.Include(x => x.Analysis).SingleAsync(x => x.Id == publicId);
            var topic = await db.Topics.Where(x => x.SubjectId != resource.SubjectId).FirstAsync();
            subjectId = topic.SubjectId; topicId = topic.Id; fileHash = resource.FileHash;
            resource.Analysis ??= new ResourceAnalysis();
            resource.Analysis.Status = AnalysisStatus.Completed; resource.Analysis.AnalyzedFileHash = fileHash;
            resource.Analysis.SuggestedSubjectId = subjectId; resource.Analysis.SuggestedTopicId = topicId;
            await db.SaveChangesAsync();
        }
        using var teacher = Client();
        await LoginAsync(teacher, TeacherEmail);
        using var rejected = await PostAsync(teacher, "/Processing/AcceptSuggestion", "/Resources/Create", new()
        {
            ["id"] = publicId.ToString(), ["fileHash"] = new string('0', 64), ["subjectId"] = subjectId.ToString(), ["topicId"] = topicId.ToString()
        });
        Redirect(rejected, $"/Resources/Details/{publicId}");
        await using (var db = database.Open()) Assert.NotEqual(subjectId, (await db.Resources.SingleAsync(x => x.Id == publicId)).SubjectId);
        using var accepted = await PostAsync(teacher, "/Processing/AcceptSuggestion", "/Resources/Create", new()
        {
            ["id"] = publicId.ToString(), ["fileHash"] = fileHash, ["subjectId"] = subjectId.ToString(), ["topicId"] = topicId.ToString()
        });
        Redirect(accepted, $"/Resources/Details/{publicId}");
        await using var verify = database.Open();
        var saved = await verify.Resources.SingleAsync(x => x.Id == publicId);
        Assert.Equal(subjectId, saved.SubjectId);
        Assert.Equal(topicId, saved.TopicId);
    }

    [SqlServerFact]
    public async Task Catalog_PDF_snippets_are_encoded_filters_survive_pagination_and_related_endpoint_filters_access()
    {
        var marker = "insidepdf" + suffix;
        int subjectId, topicId, typeId, tagId;
        await using (var db = database.Open())
        {
            var original = await db.Resources.SingleAsync(x => x.Id == publicId);
            var topic = await db.Topics.FirstAsync();
            var tag = await db.Tags.FirstAsync();
            subjectId = topic.SubjectId; topicId = topic.Id; typeId = original.ResourceTypeId; tagId = tag.Id;
            for (var i = 0; i < 14; i++)
            {
                db.Resources.Add(new Resource
                {
                    Title = $"FILTER-{suffix}-{i:00}", Description = "Каталог сүзгісінің HTTP тексеруі", OwnerId = original.OwnerId,
                    SubjectId = subjectId, TopicId = topicId, ResourceTypeId = typeId, AccessLevel = AccessLevel.Public,
                    Status = ResourceStatus.Published, OriginalFileName = original.OriginalFileName, StoredFileName = Guid.NewGuid().ToString("N") + ".pdf",
                    ContentType = "application/pdf", Extension = ".pdf", FileSize = original.FileSize, FileHash = original.FileHash,
                    ExtractedText = $"{marker} <script>alert('snippet-xss')</script>",
                    TextExtraction = new ResourceTextExtraction { Status = ExtractionStatus.Completed, ExtractedFileHash = original.FileHash, TotalPages = 1, ProcessedPages = 1, CharacterCount = 100 },
                    Analysis = new ResourceAnalysis { Status = AnalysisStatus.Completed, AnalyzedFileHash = original.FileHash, Difficulty = "Орта" },
                    ResourceTags = [new ResourceTag { TagId = tagId }]
                });
            }
            await db.SaveChangesAsync();
        }
        using var guest = Client();
        var query = $"Search={marker}&SubjectId={subjectId}&TopicId={topicId}&TypeId={typeId}&TagId={tagId}&Difficulty={Uri.EscapeDataString("Орта")}&Sort=title";
        var html = await HtmlAsync(guest, "/Resources?" + query);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("14", decoded);
        Assert.Contains("FILTER-" + suffix, decoded);
        Assert.Contains("Табылған үзінді", decoded);
        Assert.DoesNotContain("<script>alert('snippet-xss')</script>", html);
        Assert.Contains("&lt;script&gt;", html);
        var secondPage = Regex.Matches(decoded, "<a\\b[^>]*href=\"([^\"]+)\"[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()
            .Select(x => x.Groups[1].Value).First(x => x.Contains("Page=2", StringComparison.OrdinalIgnoreCase));
        foreach (var expected in new[] { "Search=" + marker, $"SubjectId={subjectId}", $"TopicId={topicId}", $"TypeId={typeId}", $"TagId={tagId}", "Difficulty=", "Sort=title" }) Assert.Contains(expected, secondPage);
        Assert.Contains($"FILTER-{suffix}-13", WebUtility.HtmlDecode(await HtmlAsync(guest, secondPage)));
        using var hiddenSource = await guest.GetAsync($"/Resources/Related/{privateId}");
        Assert.Equal(HttpStatusCode.NotFound, hiddenSource.StatusCode);
        using var related = await guest.GetAsync($"/Resources/Related/{publicId}");
        Assert.Equal(HttpStatusCode.OK, related.StatusCode);
        using var payload = JsonDocument.Parse(await related.Content.ReadAsStringAsync());
        foreach (var item in payload.RootElement.GetProperty("items").EnumerateArray())
        {
            Assert.NotEqual(publicId, item.GetProperty("id").GetInt32());
            Assert.NotEqual(privateId, item.GetProperty("id").GetInt32());
        }
    }

    [SqlServerFact]
    public async Task PDF_page_bookmarks_are_private_validate_pages_and_expire_when_file_hash_changes()
    {
        using var student = Client();
        await LoginAsync(student, StudentEmail);
        using var noToken = await student.PostAsync("/Library/SaveBookmark", new FormUrlEncodedContent(new Dictionary<string, string> { ["id"] = publicId.ToString(), ["pageNumber"] = "1" }));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using var saved = await PostAsync(student, "/Library/SaveBookmark", "/Library/Favorites", new() { ["id"] = publicId.ToString(), ["pageNumber"] = "1" });
        Redirect(saved, $"/Resources/Details/{publicId}");
        var html = await HtmlAsync(student, $"/Resources/Details/{publicId}");
        Assert.Contains("#page=1", html);
        var other = await CreateUserAsync(AppRoles.Student);
        using var stranger = Client();
        await LoginAsync(stranger, other.Email!);
        Assert.DoesNotContain("#page=1", await HtmlAsync(stranger, $"/Resources/Details/{publicId}"));
        using var invalid = await PostAsync(student, "/Library/SaveBookmark", "/Library/Favorites", new() { ["id"] = publicId.ToString(), ["pageNumber"] = "0" });
        Assert.True(invalid.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.BadRequest);
        await using (var db = database.Open())
        {
            var bookmark = Assert.Single(await db.PageBookmarks.Where(x => x.ResourceId == publicId).ToListAsync());
            Assert.Equal(1, bookmark.PageNumber);
            (await db.Resources.SingleAsync(x => x.Id == publicId)).FileHash = new string('f', 64);
            await db.SaveChangesAsync();
        }
        Assert.DoesNotContain("#page=1", await HtmlAsync(student, $"/Resources/Details/{publicId}"));
    }

    private PhaseTwoFactory Factory => factory ?? throw new InvalidOperationException("SQL test host not initialized.");
    private static string ProjectRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });
    private async Task LoginAsync(HttpClient client, string email)
    {
        using var response = await PostAsync(client, "/Account/Login", "/Account/Login", new() { ["Email"] = email, ["Password"] = password });
        Redirect(response, "/");
    }
    private async Task<ApplicationUser> CreateUserAsync(string role)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = Guid.NewGuid().ToString("N") + "@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, FullName = "Phase two user" };
        Assert.True((await users.CreateAsync(user, password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }
    private static async Task<string> HtmlAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string tokenPage, Dictionary<string, string> fields)
    {
        var html = await HtmlAsync(client, tokenPage);
        var input = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>().First(x => x.Value.Contains("name=\"__RequestVerificationToken\""));
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(input.Value, "value=\"([^\"]*)\"").Groups[1].Value);
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }
    private static void Redirect(HttpResponseMessage response, string prefix)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Assert.IsType<Uri>(response.Headers.Location);
        Assert.StartsWith(prefix, location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }
    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await database.DisposeAsync();
        var target = Path.GetFullPath(temporaryRoot);
        var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EduShelfPhaseTwoHttp")) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(Path.GetFileName(target), "^[a-f0-9]{32}$")) throw new InvalidOperationException("Unsafe test cleanup.");
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }
    private sealed class PhaseTwoFactory(string connection, string storageRoot, Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(ProjectRoot);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>(); services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
                services.Configure<StorageOptions>(options => options.RootPath = storageRoot);
                services.Configure<AiOptions>(options => options.ApiKey = "");
                services.Configure<ProcessingWorkerOptions>(options => options.Enabled = false);
            });
        }
    }
}
