using System.Text.Encodings.Web;
using System.Text.Unicode;
using EduShelf.Data;
using EduShelf.Models;
using EduShelf.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
var connection = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required. See README.md.");
builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connection));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.User.RequireUniqueEmail = true;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
    options.Lockout.MaxFailedAccessAttempts = 5;
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.Name = "EduShelf.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section).ValidateDataAnnotations().ValidateOnStart();
var uploadMb = builder.Configuration.GetValue("Storage:MaxFileSizeMb", 20);
var requestLimit = Math.Clamp(uploadMb, 1, 1024) * 1024L * 1024L + 1024 * 1024;
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = requestLimit);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = requestLimit);
builder.Services.AddScoped<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<ResourceService>();
builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<UserAdministrationService>();
builder.Services.AddSingleton(HtmlEncoder.Create(UnicodeRanges.BasicLatin, UnicodeRanges.Cyrillic, UnicodeRanges.GeneralPunctuation));
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(_ => "Енгізілген мән дұрыс емес.");
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor((_, _) => "Енгізілген мән дұрыс емес.");
    options.ModelBindingMessageProvider.SetValueMustNotBeNullAccessor(_ => "Бұл өрісті толтырыңыз.");
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(_ => "Сан енгізіңіз.");
});

var app = builder.Build();
if (args.Any(a => a is "--migrate" or "--seed" or "--check-db"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        if (args.Contains("--migrate")) { await db.Database.MigrateAsync(); Console.WriteLine("Migrations applied."); }
        if (args.Contains("--check-db"))
        {
            await db.Database.OpenConnectionAsync();
            Console.WriteLine($"SQL Server connection OK. Server version: {db.Database.GetDbConnection().ServerVersion}");
            await db.Database.CloseConnectionAsync();
        }
        if (args.Contains("--seed")) { await DemoSeeder.SeedAsync(scope.ServiceProvider, app.Environment, app.Configuration); Console.WriteLine("Seed completed."); }
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Database maintenance failed.");
        Console.Error.WriteLine("Database operation failed. Check SQL Server instance, Windows login permissions and README.md.");
        Environment.ExitCode = 1;
    }
    return;
}

// Safe error pages are used in Development too; server paths and stack traces stay in logs.
app.UseExceptionHandler("/Home/Error");
app.UseStatusCodePagesWithReExecute("/Home/StatusCodePage", "?code={0}");
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");
app.Run();

public partial class Program;
