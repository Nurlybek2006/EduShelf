using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using EduShelf.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduShelf.Tests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("EDUSHELF_TEST_CONNECTION")))
            Skip = "Set EDUSHELF_TEST_CONNECTION to a SQL Server connection allowed to CREATE DATABASE. A uniquely named temporary test database will be created and removed.";
    }
}

/// <summary>Uses the actual SQL Server provider and migrations; never touches an application database.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string databaseName = "EduShelf_Tests_" + Guid.NewGuid().ToString("N");
    private string? masterConnection;
    private bool created;
    private readonly List<string> extraDatabases = [];
    public DbContextOptions<ApplicationDbContext> Options { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("EDUSHELF_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(configured)) return;
        var builder = new SqlConnectionStringBuilder(configured) { InitialCatalog = "master" };
        masterConnection = builder.ConnectionString;
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE [{databaseName}]";
        await command.ExecuteNonQueryAsync();
        created = true;
        builder.InitialCatalog = databaseName;
        Options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(builder.ConnectionString).Options;
        await using var db = Open();
        await db.Database.MigrateAsync();
    }

    public ApplicationDbContext Open() => new(Options);

    public async Task<ApplicationDbContext> CreateFromScriptTwiceAsync(string script)
    {
        var name = "EduShelf_Tests_" + Guid.NewGuid().ToString("N");
        await using (var master = new SqlConnection(masterConnection))
        {
            await master.OpenAsync();
            await using var create = master.CreateCommand();
            create.CommandText = $"CREATE DATABASE [{name}]";
            await create.ExecuteNonQueryAsync();
            extraDatabases.Add(name);
        }
        var builder = new SqlConnectionStringBuilder(masterConnection) { InitialCatalog = name };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        var batches = Regex.Split(script, @"^\s*GO\s*(?:--[^\r\n]*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        for (var run = 0; run < 2; run++)
            foreach (var batch in batches.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                await using var command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = 120;
                await command.ExecuteNonQueryAsync();
            }
        return new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(builder.ConnectionString).Options);
    }

    public async Task<TestData> CreateCaseAsync()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var db = Open();
        var student = new ApplicationUser { Id = "student-" + suffix, UserName = "student-" + suffix, FullName = "Test student" };
        var teacher = new ApplicationUser { Id = "teacher-" + suffix, UserName = "teacher-" + suffix, FullName = "Test teacher" };
        var subject = new Subject { Name = "Subject " + suffix };
        var otherSubject = new Subject { Name = "Other " + suffix };
        var type = new ResourceType { Name = "Type " + suffix };
        var topic = new Topic { Name = "Topic " + suffix, Subject = otherSubject };
        db.AddRange(student, teacher, subject, otherSubject, type, topic);
        await db.SaveChangesAsync();
        var resource = new Resource
        {
            Title = "Public " + suffix, Description = "Integration test resource", SubjectId = subject.Id,
            ResourceTypeId = type.Id, OwnerId = teacher.Id, Status = ResourceStatus.Published,
            AccessLevel = AccessLevel.Public, OriginalFileName = "test.pdf", StoredFileName = Guid.NewGuid().ToString("N") + ".pdf",
            ContentType = "application/pdf", FileSize = 50, Extension = ".pdf", FileHash = new string('a', 64),
            Analysis = new ResourceAnalysis()
        };
        db.Resources.Add(resource);
        await db.SaveChangesAsync();
        return new TestData(student.Id, teacher.Id, subject.Id, otherSubject.Id, type.Id, topic.Id, resource.Id, suffix);
    }

    public async Task DisposeAsync()
    {
        if (!created) return;
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(masterConnection);
        await connection.OpenAsync();
        foreach (var name in extraDatabases.Append(databaseName))
        {
            // Only exact generated names recorded when CREATE succeeded can be removed.
            if (!Regex.IsMatch(name, "^EduShelf_Tests_[a-f0-9]{32}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException("Unsafe test database name.");
            await using var command = connection.CreateCommand();
            command.CommandText = $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await command.ExecuteNonQueryAsync();
        }
    }

    public sealed record TestData(string StudentId, string TeacherId, int SubjectId, int OtherSubjectId, int TypeId, int OtherTopicId, int ResourceId, string Suffix)
    {
        public ClaimsPrincipal Student => ResourceAccessTests.Principal(StudentId, AppRoles.Student);
        public ClaimsPrincipal Teacher => ResourceAccessTests.Principal(TeacherId, AppRoles.Teacher);
        public ResourceEditViewModel ValidModel => new() { Title = "Valid lesson", Description = "Valid description", SubjectId = SubjectId, ResourceTypeId = TypeId, Status = ResourceStatus.Published, AccessLevel = AccessLevel.Public };
    }
}

public class SqlServerIntegrationTests(SqlServerFixture fixture) : IClassFixture<SqlServerFixture>
{
    [SqlServerFact]
    public async Task Favorites_database_key_rejects_duplicate_user_resource_pair()
    {
        var data = await fixture.CreateCaseAsync();
        await using (var first = fixture.Open())
        {
            first.Favorites.Add(new Favorite { UserId = data.StudentId, ResourceId = data.ResourceId });
            await first.SaveChangesAsync();
        }
        await using (var second = fixture.Open())
        {
            second.Favorites.Add(new Favorite { UserId = data.StudentId, ResourceId = data.ResourceId });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
            var sqlError = Assert.IsType<SqlException>(error.InnerException);
            Assert.True(sqlError.Number is 2601 or 2627);
        }
        await using var verify = fixture.Open();
        Assert.Equal(1, await verify.Favorites.CountAsync(x => x.UserId == data.StudentId && x.ResourceId == data.ResourceId));
    }

    [SqlServerFact]
    public async Task Concurrent_favorite_toggles_serialize_without_duplicate_rows()
    {
        var data = await fixture.CreateCaseAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 9).Select(async _ =>
        {
            await using var db = fixture.Open();
            return await new LibraryService(db).ToggleFavoriteAsync(data.ResourceId, data.Student);
        }));
        Assert.Equal(5, results.Count(x => x));
        await using var verify = fixture.Open();
        Assert.Equal(1, await verify.Favorites.CountAsync(x => x.UserId == data.StudentId && x.ResourceId == data.ResourceId));
        Assert.False(await new LibraryService(verify).ToggleFavoriteAsync(data.ResourceId, data.Student));
        Assert.False(await verify.Favorites.AnyAsync(x => x.UserId == data.StudentId && x.ResourceId == data.ResourceId));
    }

    [SqlServerFact]
    public async Task Favorite_mutation_denies_anonymous_and_inaccessible_draft()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var resource = await db.Resources.FindAsync(data.ResourceId);
        resource!.Status = ResourceStatus.Draft;
        await db.SaveChangesAsync();
        var service = new LibraryService(db);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ToggleFavoriteAsync(data.ResourceId, ResourceAccessTests.Principal()));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ToggleFavoriteAsync(data.ResourceId, data.Student));
        Assert.False(await db.Favorites.AnyAsync(x => x.ResourceId == data.ResourceId));
    }

    [SqlServerFact]
    public async Task Topic_from_another_subject_is_rejected_by_server_validation()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var service = Service(db);
        var model = data.ValidModel;
        model.TopicId = data.OtherTopicId;
        await Assert.ThrowsAsync<ValidationException>(() => service.ValidateMetadataAsync(model));
        model.SubjectId = data.OtherSubjectId;
        await service.ValidateMetadataAsync(model);
    }

    [SqlServerFact]
    public async Task Database_composite_foreign_key_rejects_a_topic_from_another_subject()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        // Bypass EF relationship fixup and service validation to exercise the SQL constraint itself.
        var error = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE Resources SET TopicId={data.OtherTopicId} WHERE Id={data.ResourceId}"));
        Assert.Equal(547, error.Number);
        Assert.Null((await db.Resources.FindAsync(data.ResourceId))!.TopicId);
    }

    [SqlServerFact]
    public async Task Catalog_query_filters_private_metadata_in_SQL_before_search_and_counts()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var privateResource = new Resource
        {
            Title = "Hidden " + data.Suffix, Description = "Private metadata " + data.Suffix,
            SubjectId = data.SubjectId, ResourceTypeId = data.TypeId, OwnerId = data.TeacherId,
            AccessLevel = AccessLevel.Authenticated, Status = ResourceStatus.Published,
            OriginalFileName = "hidden.pdf", StoredFileName = Guid.NewGuid().ToString("N") + ".pdf", FileSize = 50, FileHash = new string('b', 64)
        };
        db.Resources.Add(privateResource);
        await db.SaveChangesAsync();
        var anonymous = await Service(db).CatalogAsync(new CatalogQuery { Search = data.Suffix }, ResourceAccessTests.Principal());
        Assert.Equal(1, anonymous.TotalCount);
        Assert.Equal(data.ResourceId, Assert.Single(anonymous.Items).Id);
        var signedIn = await Service(db).CatalogAsync(new CatalogQuery { Search = data.Suffix }, data.Student);
        Assert.Equal(2, signedIn.TotalCount);
    }

    [SqlServerFact]
    public async Task Database_save_failure_removes_newly_stored_file()
    {
        var data = await fixture.CreateCaseAsync();
        await using var db = fixture.Open();
        var storage = new RecordingStorage();
        var model = data.ValidModel;
        model.File = new FormFile(new MemoryStream("%PDF-1.7"u8.ToArray()), 0, 8, "file", "lesson.pdf");
        var missingUser = ResourceAccessTests.Principal("missing-" + Guid.NewGuid().ToString("N"), AppRoles.Teacher);
        await Assert.ThrowsAsync<DbUpdateException>(() => Service(db, storage).CreateAsync(model, missingUser));
        Assert.Equal(1, storage.SavedCount);
        Assert.Equal(storage.StoredName, Assert.Single(storage.Deleted));
    }

    [SqlServerFact]
    public async Task Stale_file_replacement_is_rejected_and_only_the_losing_new_file_is_removed()
    {
        var data = await fixture.CreateCaseAsync();
        await using var firstDb = fixture.Open();
        await using var staleDb = fixture.Open();
        var firstResource = await firstDb.Resources.Include(x => x.Analysis).SingleAsync(x => x.Id == data.ResourceId);
        var staleResource = await staleDb.Resources.Include(x => x.Analysis).SingleAsync(x => x.Id == data.ResourceId);
        var originalFile = firstResource.StoredFileName;
        var firstStorage = new RecordingStorage();
        var losingStorage = new RecordingStorage();
        var firstModel = data.ValidModel;
        var staleModel = data.ValidModel;
        firstModel.RowVersion = Convert.ToBase64String(firstResource.RowVersion);
        staleModel.RowVersion = Convert.ToBase64String(staleResource.RowVersion);
        firstModel.File = PdfUpload(); staleModel.File = PdfUpload();
        firstModel.Title = "First successful update";
        staleModel.Title = "Stale update";
        await Service(firstDb, firstStorage).UpdateAsync(firstResource, firstModel, data.Teacher);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Service(staleDb, losingStorage).UpdateAsync(staleResource, staleModel, data.Teacher));
        Assert.Equal(originalFile, Assert.Single(firstStorage.Deleted));
        Assert.Equal(losingStorage.StoredName, Assert.Single(losingStorage.Deleted));
        await using var verify = fixture.Open();
        var saved = await verify.Resources.SingleAsync(x => x.Id == data.ResourceId);
        Assert.Equal(firstStorage.StoredName, saved.StoredFileName);
        Assert.Equal("First successful update", saved.Title);
    }

    [SqlServerFact]
    public async Task Idempotent_SQL_script_applies_twice_and_matches_migration_schema()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var script = await File.ReadAllTextAsync(Path.Combine(sourceRoot, "scripts", "EduShelf.sql"));
        Assert.Contains("CREATE TABLE [Resources]", script);
        await using var scripted = await fixture.CreateFromScriptTwiceAsync(script);
        await using var migrated = fixture.Open();
        Assert.Equal(await SchemaSnapshotAsync(migrated), await SchemaSnapshotAsync(scripted));
        Assert.Equal(await migrated.Database.GetAppliedMigrationsAsync(), await scripted.Database.GetAppliedMigrationsAsync());
    }

    private static async Task<List<string>> SchemaSnapshotAsync(ApplicationDbContext db)
    {
        // Compare column types, keys/indexes, foreign keys and checks; database-generated object IDs are excluded.
        var query = """
            SELECT CONCAT('C|',t.name,'|',c.name,'|',TYPE_NAME(c.user_type_id),'|',c.max_length,'|',c.precision,'|',c.scale,'|',c.is_nullable,'|',c.is_identity) AS Value
            FROM sys.tables t JOIN sys.columns c ON c.object_id=t.object_id WHERE t.is_ms_shipped=0
            UNION ALL
            SELECT CONCAT('I|',t.name,'|',i.name,'|',i.is_unique,'|',i.is_primary_key,'|',i.filter_definition,'|',ic.key_ordinal,'|',ic.is_included_column,'|',ic.is_descending_key,'|',c.name)
            FROM sys.tables t JOIN sys.indexes i ON i.object_id=t.object_id JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id WHERE t.is_ms_shipped=0
            UNION ALL
            SELECT CONCAT('F|',OBJECT_NAME(f.parent_object_id),'|',f.name,'|',COL_NAME(fc.parent_object_id,fc.parent_column_id),'|',OBJECT_NAME(f.referenced_object_id),'|',COL_NAME(fc.referenced_object_id,fc.referenced_column_id),'|',f.delete_referential_action,'|',f.update_referential_action)
            FROM sys.foreign_keys f JOIN sys.foreign_key_columns fc ON fc.constraint_object_id=f.object_id
            UNION ALL SELECT CONCAT('K|',OBJECT_NAME(parent_object_id),'|',name,'|',definition) FROM sys.check_constraints
            UNION ALL SELECT CONCAT('D|',OBJECT_NAME(parent_object_id),'|',COL_NAME(parent_object_id,parent_column_id),'|',definition) FROM sys.default_constraints
            ORDER BY Value
            """;
        return await db.Database.SqlQueryRaw<string>(query).ToListAsync();
    }

    private static IFormFile PdfUpload() => new FormFile(new MemoryStream("%PDF-1.7"u8.ToArray()), 0, 8, "file", "lesson.pdf");

    [SqlServerFact]
    public async Task Repeated_demo_seed_imports_six_real_PDFs_without_duplicate_accounts_or_resources()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "EduShelfTests", suffix);
        var environment = new TestEnvironment(AppContext.BaseDirectory);
        var options = Options.Create(new StorageOptions { RootPath = temporaryRoot });
        var storage = new LocalFileStorage(environment, options);
        await using var provider = IdentityServices(storage);
        var config = DemoConfiguration(suffix);
        try
        {
            await DemoSeeder.SeedAsync(provider, environment, config);
            await DemoSeeder.SeedAsync(provider, environment, config);
            await using var db = fixture.Open();
            var teacher = await db.Users.SingleAsync(x => x.Email == config["Seed:TeacherEmail"]);
            var resources = await db.Resources.Include(x => x.Analysis).Where(x => x.OwnerId == teacher.Id).ToListAsync();
            Assert.Equal(6, resources.Count);
            Assert.Equal(6, resources.Select(x => x.StoredFileName).Distinct().Count());
            Assert.Equal(6, Directory.EnumerateFiles(temporaryRoot).Count());
            Assert.All(resources, resource =>
            {
                Assert.True(storage.Exists(resource.StoredFileName));
                Assert.Equal(AnalysisStatus.NotAnalyzed, resource.Analysis!.Status);
                Assert.Null(resource.Analysis.Summary);
                Assert.Null(resource.ExtractedText);
                Assert.True(resource.FileSize > 1000);
                using var file = File.OpenRead(storage.GetPhysicalPath(resource.StoredFileName));
                Span<byte> signature = stackalloc byte[5];
                file.ReadExactly(signature);
                Assert.True(signature.SequenceEqual("%PDF-"u8));
            });
            Assert.Equal(3, await db.Users.CountAsync(x => x.Email != null && x.Email.EndsWith(suffix + "@example.test")));
        }
        finally
        {
            // temporaryRoot is the fresh GUID child created by this test.
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [SqlServerFact]
    public async Task Production_environment_never_creates_demo_accounts_even_when_demo_setting_is_true()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var provider = IdentityServices();
        var environment = new TestEnvironment(AppContext.BaseDirectory) { EnvironmentName = "Production" };
        await DemoSeeder.SeedAsync(provider, environment, DemoConfiguration(suffix));
        await using var db = fixture.Open();
        Assert.False(await db.Users.AnyAsync(x => x.Email != null && x.Email.EndsWith(suffix + "@example.test")));
        Assert.Equal(3, await db.Roles.CountAsync(x => AppRoles.All.Contains(x.Name!)));
    }

    [SqlServerFact]
    public async Task Concurrent_administrator_demotions_preserve_one_administrator()
    {
        await using var provider = IdentityServices();
        string firstId, secondId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in AppRoles.All)
                if (!await roleManager.RoleExistsAsync(role)) Assert.True((await roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var suffix = Guid.NewGuid().ToString("N");
            var first = new ApplicationUser { UserName = "admin-first-" + suffix };
            var second = new ApplicationUser { UserName = "admin-second-" + suffix };
            Assert.True((await manager.CreateAsync(first)).Succeeded);
            Assert.True((await manager.CreateAsync(second)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(first, AppRoles.Admin)).Succeeded);
            Assert.True((await manager.AddToRoleAsync(second, AppRoles.Admin)).Succeeded);
            firstId = first.Id; secondId = second.Id;
            // Other tests can seed admins; remove those roles inside this private throwaway database.
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var adminRole = await db.Roles.SingleAsync(x => x.NormalizedName == "ADMIN");
            await db.UserRoles.Where(x => x.RoleId == adminRole.Id && x.UserId != firstId && x.UserId != secondId).ExecuteDeleteAsync();
        }
        var results = await Task.WhenAll(new[] { firstId, secondId }.Select(async id =>
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            return await new UserAdministrationService(db, manager).ChangeRoleAsync(id, id, AppRoles.Teacher);
        }));
        Assert.Single(results, x => x.Succeeded);
        await using var verify = fixture.Open();
        var roleId = await verify.Roles.Where(x => x.NormalizedName == "ADMIN").Select(x => x.Id).SingleAsync();
        Assert.Equal(1, await verify.UserRoles.CountAsync(x => x.RoleId == roleId));
    }

    private ServiceProvider IdentityServices(IFileStorage? storage = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => fixture.Open());
        services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services.AddSingleton(storage ?? new RecordingStorage());
        return services.BuildServiceProvider();
    }

    private static IConfiguration DemoConfiguration(string suffix)
    {
        var password = Guid.NewGuid().ToString("N") + "Aa!7";
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Seed:DemoEnabled"] = "true", ["Seed:AdminEmail"] = "admin-" + suffix + "@example.test", ["Seed:AdminPassword"] = password,
            ["Seed:TeacherEmail"] = "teacher-" + suffix + "@example.test", ["Seed:TeacherPassword"] = password,
            ["Seed:StudentEmail"] = "student-" + suffix + "@example.test", ["Seed:StudentPassword"] = password
        }).Build();
    }

    private static ResourceService Service(ApplicationDbContext db, IFileStorage? storage = null)
        => new(db, storage ?? new RecordingStorage(), Options.Create(new StorageOptions()), NullLogger<ResourceService>.Instance);

    private sealed class RecordingStorage : IFileStorage
    {
        public string StoredName { get; } = Guid.NewGuid().ToString("N") + ".pdf";
        public int SavedCount { get; private set; }
        public List<string> Deleted { get; } = [];
        public Task<StoredFile> SaveAsync(IFormFile file, CancellationToken cancellationToken = default)
        {
            SavedCount++;
            return Task.FromResult(new StoredFile(StoredName, "lesson.pdf", "application/pdf", 50, ".pdf", new string('a', 64)));
        }
        public void Delete(string storedName) => Deleted.Add(storedName);
        public bool Exists(string storedName) => true;
        public string GetPhysicalPath(string storedName) => throw new NotSupportedException();
    }
}
