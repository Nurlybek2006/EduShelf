using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduShelf.Tests;

/// <summary>Real MVC, Razor, Identity cookies, antiforgery, files and SQL Server for every test.
/// Each test creates and removes its own generated database; application databases are never used.</summary>
public sealed class HttpWorkflowTests : IAsyncLifetime
{
    private readonly SqlServerFixture database = new();
    private readonly string suffix = Guid.NewGuid().ToString("N");
    private readonly string temporaryRoot = Path.Combine(Path.GetTempPath(), "EduShelfHttpTests", Guid.NewGuid().ToString("N"));
    private readonly string password = "Aa9!" + Guid.NewGuid().ToString("N");
    private WorkflowFactory? factory;
    private string AdminEmail => $"admin-{suffix}@example.test";
    private string TeacherEmail => $"teacher-{suffix}@example.test";
    private string StudentEmail => $"student-{suffix}@example.test";
    private int publicId, privateId, draftId;
    private string PublicTitle => "HTTP-Public-" + suffix;
    private string PrivateTitle => "HTTP-Private-" + suffix;
    private string DraftTitle => "HTTP-Draft-" + suffix;

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EDUSHELF_TEST_CONNECTION"))) return;
        await database.InitializeAsync();
        await using var isolated = database.Open();
        var connection = isolated.Database.GetConnectionString()!;
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = connection,
            ["Storage:RootPath"] = temporaryRoot,
            ["Seed:DemoEnabled"] = "true",
            ["Seed:AdminEmail"] = AdminEmail, ["Seed:AdminPassword"] = password,
            ["Seed:TeacherEmail"] = TeacherEmail, ["Seed:TeacherPassword"] = password,
            ["Seed:StudentEmail"] = StudentEmail, ["Seed:StudentPassword"] = password,
            ["Bootstrap:AdminEmail"] = "", ["Bootstrap:AdminPassword"] = ""
        };
        factory = new WorkflowFactory(connection, temporaryRoot, settings);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var hosted = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Guard before seeding: a test host must never inherit the application's connection.
            Assert.Equal(isolated.Database.GetDbConnection().Database, hosted.Database.GetDbConnection().Database);
            Assert.StartsWith("EduShelf_Tests_", hosted.Database.GetDbConnection().Database);
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
            Assert.StartsWith(temporaryRoot + Path.DirectorySeparatorChar, storage.GetPhysicalPath(new string('a', 32) + ".pdf"));
        }
        await DemoSeeder.SeedAsync(factory.Services, factory.Services.GetRequiredService<IWebHostEnvironment>(), factory.Services.GetRequiredService<IConfiguration>());
        await using var db = database.Open();
        var teacherId = await db.Users.Where(x => x.Email == TeacherEmail).Select(x => x.Id).SingleAsync();
        var lessons = await db.Resources.Where(x => x.OwnerId == teacherId).OrderBy(x => x.Id).ToListAsync();
        var publicLesson = lessons.First(x => x.AccessLevel == AccessLevel.Public);
        var privateLesson = lessons.Single(x => x.AccessLevel == AccessLevel.Authenticated);
        var draftLesson = lessons.Last(x => x.AccessLevel == AccessLevel.Public);
        publicId = publicLesson.Id; publicLesson.Title = PublicTitle;
        privateId = privateLesson.Id; privateLesson.Title = PrivateTitle;
        draftId = draftLesson.Id; draftLesson.Title = DraftTitle; draftLesson.Status = ResourceStatus.Draft;
        await db.SaveChangesAsync();
    }

    [SqlServerFact]
    public async Task Registration_ignores_forged_admin_role_and_real_cookies_obey_login_logout_antiforgery()
    {
        using var client = Client();
        var email = $"registered-{suffix}@example.test";
        using var registered = await PostWithTokenAsync(client, "/Account/Register", "/Account/Register", new()
        {
            ["FullName"] = "HTTP Learner", ["Email"] = email, ["Password"] = password, ["ConfirmPassword"] = password,
            ["Role"] = AppRoles.Admin, ["Roles"] = AppRoles.Admin, ["ReturnUrl"] = "https://attacker.example/"
        });
        AssertRedirect(registered, "/");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.NotNull(user);
            Assert.Equal([AppRoles.Student], await users.GetRolesAsync(user));
        }
        using var forbidden = await client.GetAsync("/Resources/Create");
        AssertRedirect(forbidden, "/Account/AccessDenied");
        using var csrf = await client.PostAsync("/Account/Logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var stillSignedIn = await client.GetAsync("/Library/Favorites");
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        using var getLogout = await client.GetAsync("/Account/Logout");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, getLogout.StatusCode);
        using var logout = await PostWithTokenAsync(client, "/Account/Logout", "/Library/Favorites", []);
        AssertRedirect(logout, "/");
        using var anonymous = await client.GetAsync("/Library/Favorites");
        AssertRedirect(anonymous, "/Account/Login");
        await LoginAsync(client, email, "//attacker.example/");
        using var favorites = await client.GetAsync("/Library/Favorites");
        Assert.Equal(HttpStatusCode.OK, favorites.StatusCode);
    }

    [SqlServerFact]
    public async Task Anonymous_metadata_and_files_are_filtered_and_another_teacher_cannot_edit_by_ID()
    {
        using var guest = Client();
        var catalog = await HtmlAsync(guest, "/Resources?Search=" + suffix);
        Assert.Contains(PublicTitle, catalog);
        Assert.DoesNotContain(PrivateTitle, catalog);
        Assert.DoesNotContain(DraftTitle, catalog);
        var home = await HtmlAsync(guest, "/");
        Assert.DoesNotContain(PrivateTitle, home);
        Assert.DoesNotContain(DraftTitle, home);
        var token = await TokenAsync(guest, "/Account/Login");
        foreach (var id in new[] { privateId, draftId })
        {
            using var details = await guest.GetAsync($"/Resources/Details/{id}");
            using var preview = await guest.GetAsync($"/Resources/Preview/{id}");
            using var download = await PostAsync(guest, $"/Resources/Download/{id}", [], token);
            Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
            Assert.DoesNotContain(id == privateId ? PrivateTitle : DraftTitle, await details.Content.ReadAsStringAsync());
        }

        var otherTeacher = await CreateUserAsync(AppRoles.Teacher);
        using var teacher = Client();
        await LoginAsync(teacher, otherTeacher.Email!);
        var signedInCatalog = await HtmlAsync(teacher, "/Resources?Search=" + suffix);
        Assert.Contains(PrivateTitle, signedInCatalog);
        Assert.DoesNotContain(DraftTitle, signedInCatalog);
        using var edit = await teacher.GetAsync($"/Resources/Edit/{publicId}");
        AssertRedirect(edit, "/Account/AccessDenied");
        using var forged = await PostWithTokenAsync(teacher, $"/Resources/Edit/{publicId}", "/Resources/Create", new()
        {
            ["Title"] = "Forged replacement", ["Description"] = "Forged description", ["OwnerId"] = otherTeacher.Id
        });
        AssertRedirect(forged, "/Account/AccessDenied");
        using var archived = await PostWithTokenAsync(teacher, "/Resources/SetStatus", "/Resources/Create", new()
        {
            ["id"] = publicId.ToString(), ["status"] = "Archived"
        });
        AssertRedirect(archived, "/Account/AccessDenied");
        await using var db = database.Open();
        var saved = await db.Resources.SingleAsync(x => x.Id == publicId);
        Assert.Equal(PublicTitle, saved.Title);
        Assert.Equal(ResourceStatus.Published, saved.Status);
        Assert.NotEqual(otherTeacher.Id, saved.OwnerId);
    }

    [SqlServerFact]
    public async Task PDF_preview_ranges_do_not_count_but_guest_download_and_private_history_do()
    {
        using var guest = Client();
        var token = await TokenAsync(guest, $"/Resources/Details/{publicId}");
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.ResourceViews.CountAsync(x => x.ResourceId == publicId));
            Assert.Equal(0, await db.ResourceDownloads.CountAsync(x => x.ResourceId == publicId));
        }
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/Resources/Preview/{publicId}");
            request.Headers.Range = new RangeHeaderValue(0, 31);
            using var range = await guest.SendAsync(request);
            Assert.Equal(HttpStatusCode.PartialContent, range.StatusCode);
            Assert.Equal("application/pdf", range.Content.Headers.ContentType?.MediaType);
            Assert.Equal(32, (await range.Content.ReadAsByteArrayAsync()).Length);
            Assert.Equal("inline", range.Content.Headers.ContentDisposition?.DispositionType);
            Assert.Contains("no-store", range.Headers.CacheControl!.ToString());
            Assert.Equal("nosniff", Assert.Single(range.Headers.GetValues("X-Content-Type-Options")));
            Assert.Contains("sandbox", Assert.Single(range.Headers.GetValues("Content-Security-Policy")));
        }
        using var preview = await guest.GetAsync($"/Resources/Preview/{publicId}");
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        using var missingToken = await guest.PostAsync($"/Resources/Download/{publicId}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, missingToken.StatusCode);
        using var download = await PostAsync(guest, $"/Resources/Download/{publicId}", [], token);
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        Assert.True((await download.Content.ReadAsByteArrayAsync()).AsSpan(0, 5).SequenceEqual("%PDF-"u8));
        await using (var db = database.Open())
        {
            Assert.Equal(1, await db.ResourceViews.CountAsync(x => x.ResourceId == publicId));
            Assert.Null((await db.ResourceDownloads.SingleAsync(x => x.ResourceId == publicId)).UserId);
        }

        using var student = Client();
        await LoginAsync(student, StudentEmail);
        var studentToken = await TokenAsync(student, $"/Resources/Details/{publicId}");
        using var studentDownload = await PostAsync(student, $"/Resources/Download/{publicId}", [], studentToken);
        Assert.Equal(HttpStatusCode.OK, studentDownload.StatusCode);
        using var favorite = await PostAsync(student, "/Library/ToggleFavorite", new() { ["id"] = publicId.ToString() }, studentToken);
        AssertRedirect(favorite, $"/Resources/Details/{publicId}");
        Assert.Contains(PublicTitle, await HtmlAsync(student, "/Library/Favorites"));
        Assert.Contains(PublicTitle, await HtmlAsync(student, "/Library/History"));
        var stranger = await CreateUserAsync(AppRoles.Student);
        using var another = Client();
        await LoginAsync(another, stranger.Email!);
        Assert.DoesNotContain(PublicTitle, await HtmlAsync(another, "/Library/History"));
        Assert.DoesNotContain(PublicTitle, await HtmlAsync(another, "/Library/Favorites"));
        using var removed = await PostWithTokenAsync(student, "/Library/ToggleFavorite", "/Library/Favorites", new() { ["id"] = publicId.ToString() });
        AssertRedirect(removed, $"/Resources/Details/{publicId}");
        await using (var db = database.Open())
        {
            var studentId = await db.Users.Where(x => x.Email == StudentEmail).Select(x => x.Id).SingleAsync();
            Assert.False(await db.Favorites.AnyAsync(x => x.UserId == studentId && x.ResourceId == publicId));
            Assert.Single(await db.ResourceViews.Where(x => x.UserId == studentId && x.ResourceId == publicId).ToListAsync());
            Assert.Single(await db.ResourceDownloads.Where(x => x.UserId == studentId && x.ResourceId == publicId).ToListAsync());
        }
    }

    [SqlServerFact]
    public async Task Admin_lookup_CRUD_allows_empty_topic_moves_and_blocks_referenced_deletes_and_moves()
    {
        using var admin = Client();
        await LoginAsync(admin, AdminEmail);
        var name = "HTTP lookup " + suffix;
        using var csrf = await admin.PostAsync("/Admin/EditLookup", new FormUrlEncodedContent(new Dictionary<string, string> { ["Kind"] = "subjects", ["Name"] = name }));
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        var firstSubject = await AddLookupAsync(admin, "subjects", name);
        var secondSubject = await AddLookupAsync(admin, "subjects", name + " second");
        var topic = await AddLookupAsync(admin, "topics", name + " topic", firstSubject);
        using var moved = await SaveLookupAsync(admin, "topics", topic, name + " moved", secondSubject);
        AssertRedirect(moved, "/Admin/Lookups");
        await using (var db = database.Open())
        {
            Assert.Equal(secondSubject, (await db.Topics.SingleAsync(x => x.Id == topic)).SubjectId);
            var resource = await db.Resources.SingleAsync(x => x.Id == publicId);
            resource.SubjectId = secondSubject; resource.TopicId = topic;
            await db.SaveChangesAsync();
        }
        using var blockedMove = await SaveLookupAsync(admin, "topics", topic, name + " invalid", firstSubject);
        Assert.Equal(HttpStatusCode.OK, blockedMove.StatusCode);
        Assert.Contains("пәнін өзгертуге болмайды", WebUtility.HtmlDecode(await blockedMove.Content.ReadAsStringAsync()));
        foreach (var item in new[] { (Kind: "topics", Id: topic), (Kind: "subjects", Id: secondSubject) })
        {
            using var blocked = await DeleteLookupAsync(admin, item.Kind, item.Id);
            AssertRedirect(blocked, "/Admin/Lookups");
            Assert.Contains("жойылмайды", WebUtility.HtmlDecode(await HtmlAsync(admin, "/Admin/Lookups?kind=" + item.Kind)));
        }
        foreach (var kind in new[] { "types", "tags" })
        {
            var id = await AddLookupAsync(admin, kind, name + kind);
            using var edited = await SaveLookupAsync(admin, kind, id, name + kind + " updated");
            AssertRedirect(edited, "/Admin/Lookups");
            Assert.Contains(name + kind + " updated", await HtmlAsync(admin, "/Admin/Lookups?kind=" + kind));
            using var deleted = await DeleteLookupAsync(admin, kind, id);
            AssertRedirect(deleted, "/Admin/Lookups");
            Assert.DoesNotContain(name + kind + " updated", await HtmlAsync(admin, "/Admin/Lookups?kind=" + kind));
        }
        await using (var db = database.Open())
        {
            Assert.Equal(secondSubject, (await db.Topics.SingleAsync(x => x.Id == topic)).SubjectId);
            var resource = await db.Resources.SingleAsync(x => x.Id == publicId);
            resource.TopicId = null;
            await db.SaveChangesAsync();
        }
        using var deletedTopic = await DeleteLookupAsync(admin, "topics", topic);
        using var editedSubject = await SaveLookupAsync(admin, "subjects", firstSubject, name + " renamed");
        using var deletedSubject = await DeleteLookupAsync(admin, "subjects", firstSubject);
        AssertRedirect(deletedTopic, "/Admin/Lookups");
        AssertRedirect(editedSubject, "/Admin/Lookups");
        AssertRedirect(deletedSubject, "/Admin/Lookups");
        await using var verify = database.Open();
        Assert.False(await verify.Topics.AnyAsync(x => x.Id == topic));
        Assert.False(await verify.Subjects.AnyAsync(x => x.Id == firstSubject));
    }

    [SqlServerFact]
    public async Task Admin_role_POST_blocks_last_admin_invalidates_cookies_and_serializes_concurrent_demotions()
    {
        using var admin = Client();
        await LoginAsync(admin, AdminEmail);
        string administratorId;
        await using (var db = database.Open()) administratorId = await db.Users.Where(x => x.Email == AdminEmail).Select(x => x.Id).SingleAsync();
        using var denied = await PostWithTokenAsync(admin, "/Admin/ChangeRole", "/Admin/Users", new() { ["UserId"] = administratorId, ["Role"] = "Student" });
        AssertRedirect(denied, "/Admin/Users");
        Assert.Contains("Соңғы әкімшінің рөлін өзгертуге болмайды", WebUtility.HtmlDecode(await HtmlAsync(admin, "/Admin/Users")));
        var second = await CreateUserAsync(AppRoles.Admin);
        using var secondClient = Client();
        await LoginAsync(secondClient, second.Email!);
        using var initialAccess = await secondClient.GetAsync("/Admin");
        Assert.Equal(HttpStatusCode.OK, initialAccess.StatusCode);
        using var demoted = await PostWithTokenAsync(admin, "/Admin/ChangeRole", "/Admin/Users", new() { ["UserId"] = second.Id, ["Role"] = "Student" });
        AssertRedirect(demoted, "/Admin/Users");
        using var revokedCookie = await secondClient.GetAsync("/Admin");
        AssertRedirect(revokedCookie, "/Account/Login");

        var concurrentAdmin = await CreateUserAsync(AppRoles.Admin);
        using var concurrentClient = Client();
        await LoginAsync(concurrentClient, concurrentAdmin.Email!);
        var firstToken = await TokenAsync(admin, "/Admin/Users");
        var secondToken = await TokenAsync(concurrentClient, "/Admin/Users");
        var changes = await Task.WhenAll(
            PostAsync(admin, "/Admin/ChangeRole", new() { ["UserId"] = administratorId, ["Role"] = "Student" }, firstToken),
            PostAsync(concurrentClient, "/Admin/ChangeRole", new() { ["UserId"] = concurrentAdmin.Id, ["Role"] = "Student" }, secondToken));
        foreach (var change in changes) { Assert.Equal(HttpStatusCode.Redirect, change.StatusCode); change.Dispose(); }
        await using var verify = database.Open();
        var roleId = await verify.Roles.Where(x => x.Name == AppRoles.Admin).Select(x => x.Id).SingleAsync();
        Assert.Equal(1, await verify.UserRoles.CountAsync(x => x.RoleId == roleId));
    }

    [SqlServerFact]
    public async Task Teacher_HTTP_upload_and_tag_edits_preserve_files_and_reject_stale_forms()
    {
        using var teacher = Client();
        await LoginAsync(teacher, TeacherEmail);
        int subject, type;
        List<int> tags;
        await using (var db = database.Open())
        {
            subject = await db.Subjects.Select(x => x.Id).FirstAsync();
            type = await db.ResourceTypes.Select(x => x.Id).FirstAsync();
            tags = await db.Tags.Select(x => x.Id).Take(3).ToListAsync();
        }
        var title = "HTTP uploaded " + suffix;
        var fields = new List<KeyValuePair<string, string>>
        {
            new("Title", title), new("Description", "HTTP integration upload"), new("SubjectId", subject.ToString()),
            new("ResourceTypeId", type.ToString()), new("Status", "Published"), new("AccessLevel", "Public"),
            new("OwnerId", "forged-owner"), new("TagIds", tags[0].ToString()), new("TagIds", tags[1].ToString()),
            new("__RequestVerificationToken", await TokenAsync(teacher, "/Resources/Create"))
        };
        using var multipart = new MultipartFormDataContent();
        foreach (var field in fields) multipart.Add(new StringContent(field.Value), field.Key);
        var pdf = await File.ReadAllBytesAsync(Path.Combine(ProjectRoot, "DemoFiles", "algebra-linear-equations.pdf"));
        var file = new ByteArrayContent(pdf); file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(file, "File", "../../lesson.pdf");
        using var uploaded = await teacher.PostAsync("/Resources/Create", multipart);
        AssertRedirect(uploaded, "/Resources/Details/");
        int id;
        string originalStored;
        await using (var db = database.Open())
        {
            var resource = await db.Resources.Include(x => x.ResourceTags).SingleAsync(x => x.Title == title);
            id = resource.Id; originalStored = resource.StoredFileName;
            Assert.Equal(await db.Users.Where(x => x.Email == TeacherEmail).Select(x => x.Id).SingleAsync(), resource.OwnerId);
            Assert.Equal("lesson.pdf", resource.OriginalFileName);
            Assert.Equal(tags.Take(2).Order(), resource.ResourceTags.Select(x => x.TagId).Order());
            Assert.Equal(pdf, await File.ReadAllBytesAsync(Path.Combine(temporaryRoot, originalStored)));
        }
        var form = await HtmlAsync(teacher, $"/Resources/Edit/{id}");
        var version = InputValue(form, "RowVersion");
        var token = InputValue(form, "__RequestVerificationToken");
        var edit = new List<KeyValuePair<string, string>>
        {
            new("Title", title + " edited"), new("Description", "Edited description"), new("SubjectId", subject.ToString()),
            new("ResourceTypeId", type.ToString()), new("Status", "Published"), new("AccessLevel", "Public"),
            new("RowVersion", version), new("TagIds", tags[1].ToString()), new("TagIds", tags[2].ToString()),
            new("__RequestVerificationToken", token)
        };
        using var saved = await teacher.PostAsync($"/Resources/Edit/{id}", new FormUrlEncodedContent(edit));
        AssertRedirect(saved, $"/Resources/Details/{id}");
        edit[0] = new("Title", title + " stale");
        using var stale = await teacher.PostAsync($"/Resources/Edit/{id}", new FormUrlEncodedContent(edit));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("басқа сұраныс өзгертті", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        await using var verify = database.Open();
        var final = await verify.Resources.Include(x => x.ResourceTags).SingleAsync(x => x.Id == id);
        Assert.Equal(title + " edited", final.Title);
        Assert.Equal(tags.Skip(1).Order(), final.ResourceTags.Select(x => x.TagId).Order());
        Assert.Equal(originalStored, final.StoredFileName);
        Assert.True(File.Exists(Path.Combine(temporaryRoot, originalStored)));
    }

    private WorkflowFactory Factory => factory ?? throw new InvalidOperationException("SQL HTTP test host was not initialized.");
    private static string ProjectRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private HttpClient Client() => Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true, BaseAddress = new Uri("https://localhost") });

    private async Task LoginAsync(HttpClient client, string email, string? returnUrl = null)
    {
        using var response = await PostWithTokenAsync(client, "/Account/Login", "/Account/Login", new()
        {
            ["Email"] = email, ["Password"] = password, ["ReturnUrl"] = returnUrl ?? "/"
        });
        AssertRedirect(response, "/");
    }

    private async Task<ApplicationUser> CreateUserAsync(string role)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { Email = email, UserName = email, FullName = "HTTP " + role };
        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join(",", created.Errors.Select(x => x.Code)));
        Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private static async Task<string> HtmlAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
    private static async Task<string> TokenAsync(HttpClient client, string path) => InputValue(await HtmlAsync(client, path), "__RequestVerificationToken");
    private static string InputValue(string html, string name)
    {
        var tag = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Cast<Match>()
            .FirstOrDefault(x => Regex.IsMatch(x.Value, "\\bname=\"" + Regex.Escape(name) + "\"", RegexOptions.IgnoreCase));
        Assert.NotNull(tag);
        var value = Regex.Match(tag.Value, "\\bvalue=\"([^\"]*)\"", RegexOptions.IgnoreCase);
        Assert.True(value.Success, $"Input {name} has no value.");
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, Dictionary<string, string> fields, string token)
    {
        var data = new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token };
        return client.PostAsync(path, new FormUrlEncodedContent(data));
    }
    private static async Task<HttpResponseMessage> PostWithTokenAsync(HttpClient client, string path, string tokenPage, Dictionary<string, string> fields)
        => await PostAsync(client, path, fields, await TokenAsync(client, tokenPage));
    private static void AssertRedirect(HttpResponseMessage response, string locationPrefix)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var location = response.Headers.Location;
        if (location.IsAbsoluteUri)
        {
            Assert.Equal("https", location.Scheme);
            Assert.Equal("localhost", location.Host);
        }
        Assert.StartsWith(locationPrefix, location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString);
    }
    private Task<HttpResponseMessage> SaveLookupAsync(HttpClient client, string kind, int id, string name, int? subject = null)
        => PostWithTokenAsync(client, "/Admin/EditLookup", $"/Admin/EditLookup?kind={kind}&id={id}", new()
        {
            ["Kind"] = kind, ["Id"] = id.ToString(), ["Name"] = name, ["SubjectId"] = subject?.ToString() ?? ""
        });
    private Task<HttpResponseMessage> DeleteLookupAsync(HttpClient client, string kind, int id)
        => PostWithTokenAsync(client, "/Admin/DeleteLookup", "/Admin/Lookups?kind=" + kind, new() { ["kind"] = kind, ["id"] = id.ToString() });
    private async Task<int> AddLookupAsync(HttpClient client, string kind, string name, int? subject = null)
    {
        using var response = await SaveLookupAsync(client, kind, 0, name, subject);
        AssertRedirect(response, "/Admin/Lookups");
        await using var db = database.Open();
        return kind switch
        {
            "subjects" => await db.Subjects.Where(x => x.Name == name).Select(x => x.Id).SingleAsync(),
            "topics" => await db.Topics.Where(x => x.Name == name && x.SubjectId == subject).Select(x => x.Id).SingleAsync(),
            "types" => await db.ResourceTypes.Where(x => x.Name == name).Select(x => x.Id).SingleAsync(),
            _ => await db.Tags.Where(x => x.Name == name).Select(x => x.Id).SingleAsync()
        };
    }

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await database.DisposeAsync();
        var allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "EduShelfHttpTests")) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(temporaryRoot);
        if (!target.StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(Path.GetFileName(target), "^[a-f0-9]{32}$"))
            throw new InvalidOperationException("Unsafe HTTP test file cleanup path.");
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    private sealed class WorkflowFactory(string connection, string storageRoot, Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(ProjectRoot);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<ApplicationDbContext>();
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
                services.Configure<StorageOptions>(options => options.RootPath = storageRoot);
            });
        }
    }
}
