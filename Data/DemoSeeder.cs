using EduShelf.Models;
using EduShelf.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EduShelf.Data;

/// <summary>Explicit --seed initialization. Demo identities are restricted to Development.</summary>
public static class DemoSeeder
{
    private sealed record DemoLesson(string FileName, string Title, string Description, string Subject,
        string Topic, string[] Tags, AccessLevel Access = AccessLevel.Public);

    private static readonly DemoLesson[] Lessons =
    [
        new("algebra-linear-equations.pdf", "Сызықтық теңдеулер", "7-сыныпқа арналған оқу парағы: теңдеуді қадаммен шешу, тексеру және жауаптары бар үш тапсырма.", "Математика", "Алгебра", ["7-сынып", "Өздік жұмыс"]),
        new("geometry-triangle-area.pdf", "Үшбұрыштың ауданы", "Табан мен биіктік арқылы аудан табу: формула, шешу үлгісі және жауаптары бар жаттығулар.", "Математика", "Геометрия", ["7-сынып", "Практика"]),
        new("physics-uniform-motion.pdf", "Бірқалыпты қозғалыс", "Жол, жылдамдық пен уақыттың байланысы және өлшем бірліктерін түрлендіруге арналған есептер.", "Физика", "Механика", ["7-сынып", "Практика"]),
        new("informatics-algorithms.pdf", "Алгоритм құру", "Сызықтық, тармақталу және қайталану құрылымдарын түсіндіретін қысқа оқу парағы.", "Информатика", "Алгоритмдер", ["6-сынып", "Өздік жұмыс"]),
        new("kazakh-text-main-idea.pdf", "Мәтіннің негізгі ойы", "Авторлық шағын мәтін, тақырып пен негізгі ойды анықтауға арналған тапсырмалар және жауап үлгісі.", "Қазақ тілі", "Мәтінмен жұмыс", ["6-сынып", "Оқу сауаттылығы"]),
        new("biology-plant-cell.pdf", "Өсімдік жасушасы", "Жасуша бөліктерінің қызметін байланыстыру: қысқаша түсіндіру, тапсырмалар және өзін-өзі тексеру.", "Биология", "Жасуша", ["7-сынып", "Өздік жұмыс"], AccessLevel.Authenticated)
    ];

    public static async Task SeedAsync(IServiceProvider services, IWebHostEnvironment environment, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("EduShelf.Seed");

        foreach (var role in AppRoles.All)
            if (!await roles.RoleExistsAsync(role))
                RequireSuccess(await roles.CreateAsync(new IdentityRole(role)), "Рөлді жасау");

        await SeedReferenceDataAsync(db);

        // Bootstrap is a separate, explicit operator setting; never reuse demo credentials in production.
        var bootstrapEmail = configuration["Bootstrap:AdminEmail"];
        var bootstrapPassword = configuration["Bootstrap:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(bootstrapEmail) || !string.IsNullOrWhiteSpace(bootstrapPassword))
        {
            if (string.IsNullOrWhiteSpace(bootstrapEmail) || string.IsNullOrWhiteSpace(bootstrapPassword))
                throw new InvalidOperationException("Bootstrap:AdminEmail және Bootstrap:AdminPassword екеуін де конфигурацияда орнатыңыз.");
            await EnsureAccountAsync(users, bootstrapEmail, bootstrapPassword, "Жүйе әкімшісі", AppRoles.Admin);
        }

        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Seed:DemoEnabled"))
        {
            logger.LogInformation("Roles and reference data seeded. Development demo import is disabled.");
            return;
        }

        string[] required = ["Seed:AdminEmail", "Seed:AdminPassword", "Seed:TeacherEmail", "Seed:TeacherPassword", "Seed:StudentEmail", "Seed:StudentPassword"];
        var missing = required.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException("Демо деректер үшін user-secrets немесе environment variables арқылы мына параметрлерді орнатыңыз: " + string.Join(", ", missing));

        var emails = new[] { configuration["Seed:AdminEmail"]!, configuration["Seed:TeacherEmail"]!, configuration["Seed:StudentEmail"]! };
        if (emails.Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != emails.Length)
            throw new InvalidOperationException("Үш демо рөл үшін бөлек email мекенжайларын орнатыңыз.");

        await EnsureAccountAsync(users, emails[0], configuration["Seed:AdminPassword"]!, "Демо әкімші", AppRoles.Admin);
        var teacher = await EnsureAccountAsync(users, emails[1], configuration["Seed:TeacherPassword"]!, "Айдана Мұғалім", AppRoles.Teacher);
        await EnsureAccountAsync(users, emails[2], configuration["Seed:StudentPassword"]!, "Демо оқушы", AppRoles.Student);

        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        foreach (var lesson in Lessons)
        {
            var sourcePath = Path.Combine(environment.ContentRootPath, "DemoFiles", lesson.FileName);
            if (!File.Exists(sourcePath))
                throw new InvalidOperationException($"Демо PDF табылмады: {lesson.FileName}. DemoFiles каталогын deployment құрамына қосыңыз.");

            // A manifest filename and teacher identify this seeded item; repeated seeding preserves user edits.
            var existing = await db.Resources.SingleOrDefaultAsync(x => x.OwnerId == teacher.Id && x.OriginalFileName == lesson.FileName);
            if (existing is not null && storage.Exists(existing.StoredFileName))
                continue;

            await using var stream = File.OpenRead(sourcePath);
            var upload = new FormFile(stream, 0, stream.Length, "file", lesson.FileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };
            var stored = await storage.SaveAsync(upload);
            var oldStoredName = existing?.StoredFileName;
            try
            {
                var resource = existing ?? new Resource
                {
                    Title = lesson.Title,
                    Description = lesson.Description,
                    SubjectId = await db.Subjects.Where(x => x.Name == lesson.Subject).Select(x => x.Id).SingleAsync(),
                    TopicId = await db.Topics.Where(x => x.Subject.Name == lesson.Subject && x.Name == lesson.Topic).Select(x => x.Id).SingleAsync(),
                    ResourceTypeId = await db.ResourceTypes.Where(x => x.Name == "Оқу парағы").Select(x => x.Id).SingleAsync(),
                    OwnerId = teacher.Id,
                    Status = ResourceStatus.Published,
                    AccessLevel = lesson.Access,
                    CreatedAtUtc = DateTime.UtcNow,
                    Analysis = new ResourceAnalysis { Status = AnalysisStatus.NotAnalyzed }
                };
                resource.StoredFileName = stored.StoredFileName;
                resource.OriginalFileName = stored.OriginalFileName;
                resource.ContentType = stored.ContentType;
                resource.FileSize = stored.FileSize;
                resource.Extension = stored.Extension;
                resource.FileHash = stored.FileHash;
                resource.UpdatedAtUtc = DateTime.UtcNow;
                if (existing is null)
                {
                    var tagIds = await db.Tags.Where(x => lesson.Tags.Contains(x.Name)).Select(x => x.Id).ToListAsync();
                    resource.ResourceTags = tagIds.Select(id => new ResourceTag { TagId = id }).ToList();
                    db.Resources.Add(resource);
                }
                await db.SaveChangesAsync();
            }
            catch
            {
                storage.Delete(stored.StoredFileName);
                throw;
            }
            if (!string.IsNullOrWhiteSpace(oldStoredName))
                storage.Delete(oldStoredName);
        }
        logger.LogInformation("Development demo initialization completed. No synthetic activity or AI results were created.");
    }

    private static async Task SeedReferenceDataAsync(ApplicationDbContext db)
    {
        foreach (var name in Lessons.Select(x => x.Subject).Distinct())
            if (!await db.Subjects.AnyAsync(x => x.Name == name))
                db.Subjects.Add(new Subject { Name = name, Description = $"{name} пәні бойынша оқу материалдары" });
        foreach (var name in new[] { "Оқу парағы", "Дәріс", "Практикалық жұмыс", "Әдістемелік нұсқаулық" })
            if (!await db.ResourceTypes.AnyAsync(x => x.Name == name))
                db.ResourceTypes.Add(new ResourceType { Name = name });
        foreach (var name in Lessons.SelectMany(x => x.Tags).Distinct())
            if (!await db.Tags.AnyAsync(x => x.Name == name))
                db.Tags.Add(new Tag { Name = name });
        await db.SaveChangesAsync();

        foreach (var lesson in Lessons)
        {
            var subjectId = await db.Subjects.Where(x => x.Name == lesson.Subject).Select(x => x.Id).SingleAsync();
            if (!await db.Topics.AnyAsync(x => x.SubjectId == subjectId && x.Name == lesson.Topic))
                db.Topics.Add(new Topic { SubjectId = subjectId, Name = lesson.Topic });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureAccountAsync(UserManager<ApplicationUser> users, string email, string password, string fullName, string role)
    {
        email = email.Trim();
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser { UserName = email, Email = email, FullName = fullName, EmailConfirmed = true };
            RequireSuccess(await users.CreateAsync(user, password), "Қолданушыны жасау");
        }
        if (!await users.IsInRoleAsync(user, role))
            RequireSuccess(await users.AddToRoleAsync(user, role), "Қолданушы рөлін тағайындау");
        return user;
    }

    private static void RequireSuccess(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException($"{operation} сәтсіз: {string.Join(", ", result.Errors.Select(x => x.Code))}. Identity құпиясөз талаптарын және конфигурацияны тексеріңіз.");
    }
}
