using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.Infrastructure.Seeding;

namespace FitEdu.Web.Startup;

/// <summary>
/// Startup migration + seed: pre-warms LocalDB in Development, migrates the relational
/// database, and seeds roles, subscription plans, and the admin user unconditionally, plus
/// (SeedData:Enabled in Development; a public demo also needs SeedData:AllowOutsideDevelopment) demo blog articles and JSON-driven exercise-library/
/// course/assessment/student/learning-path/testimonial/live-class sample data — none of which
/// belongs in a real deployment.
/// </summary>
public static class DatabaseSeedingExtensions
{
    /// <summary>
    /// Bounded retry-with-backoff wrapper around <see cref="PrewarmAndSeedDatabaseAsync"/> (AVAIL-2):
    /// without this, an unreachable database at boot crashes the process before it binds a port,
    /// forcing an external restart even when the database becomes reachable moments later.
    /// Every seed step below is idempotent (checks existence before creating), so retrying the
    /// whole call — not just the migration — is safe even if an earlier attempt partially seeded.
    /// </summary>
    public static async Task PrewarmAndSeedDatabaseWithRetryAsync(
        this WebApplication app, int maxAttempts = 5, TimeSpan? initialDelay = null)
    {
        var delay = initialDelay ?? TimeSpan.FromSeconds(2);
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await app.PrewarmAndSeedDatabaseAsync();
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                StartupLog.MigrateSeedRetrying(logger, ex, attempt, maxAttempts, delay);
                await Task.Delay(delay);
                delay += delay; // exponential backoff
            }
            catch (Exception ex)
            {
                StartupLog.MigrateSeedFailed(logger, ex, maxAttempts);
                throw;
            }
        }
    }

    public static async Task PrewarmAndSeedDatabaseAsync(this WebApplication app)
    {
        // --- Pre-warm LocalDB ---
        if (app.Environment.IsDevelopment())
        {
            try
            {
                using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "sqllocaldb",
                    Arguments = "start MSSQLLocalDB",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                proc?.WaitForExit(10_000);
            }
            catch { /* sqllocaldb not on PATH — non-fatal */ }
        }

        // --- Migrate & Seed ---
        using var scope = app.Services.CreateScope();
        var config = app.Configuration;
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        // Relational-only: integration tests swap in the EF Core InMemory provider, which
        // cannot run migrations. Guarding keeps the test host bootable; in production the
        // SQL Server provider is always relational so behavior is unchanged.
        if (db.Database.IsRelational())
        {
            // The migration chain is authored for SQL Server (nvarchar(max) column types etc.)
            // and does not translate to SQLite. The SQLite-backed test host (TEST-1's
            // SqliteWebAppFactory) therefore builds its schema from the current model with
            // EnsureCreated — never migrations. Production (SQL Server) is unaffected.
            if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
                await db.Database.EnsureCreatedAsync();
            else
                await db.Database.MigrateAsync();
        }

        // Seed Roles
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        string[] roles = ["Admin", "Student"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed Subscription Plans
        await SubscriptionPlanSeeder.SeedAsync(db, startupLogger);

        // Seed Admin User
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var adminEmail = config["AdminUser:Email"] ?? "admin@fitedu.local";
        var adminPassword = config["AdminUser:Password"];
        if (string.IsNullOrWhiteSpace(adminPassword))
            throw new InvalidOperationException(
                "AdminUser:Password is required. Set it via User Secrets (dev) or environment variable AdminUser__Password (prod).");

        if (await userManager.FindByEmailAsync(adminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                // Members see this name as the host of every live class the admin runs.
                FirstName = "FitEdu",
                LastName = "Coach",
                EmailConfirmed = true,
                IsEnabled = true,
                GdprConsentGiven = true,
                GdprConsentDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };

            var result = await userManager.CreateAsync(admin, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(admin, "Admin");
                StartupLog.AdminSeeded(startupLogger, adminEmail);
            }
            else
            {
                // REL-4: fail-fast, consistent with the AdminUser:Password-missing throw above —
                // starting with no admin account and no signal is worse than not starting at all.
                var errors = string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}"));
                StartupLog.AdminSeedFailed(startupLogger, adminEmail, errors);
                throw new InvalidOperationException($"Failed to seed admin user '{adminEmail}': {errors}");
            }
        }

        // Sample content (OPS-1: it must never reach a real deployment by accident — roles/plans/admin
        // above are the only seeds safe to run unconditionally). Development seeds it with SeedData:Enabled;
        // a public demo has to opt in a second time with SeedData:AllowOutsideDevelopment.
        if (SeedsSampleContent(app.Environment, config))
        {
            await BlogArticleSeeder.SeedAsync(db, startupLogger);

            // Exercises before courses, so course lessons can link exercises by slug.
            var exerciseJsonPath = SeedJsonPath(app, config, "Exercises");
            await ExerciseSeeder.SeedFromJsonAsync(db, exerciseJsonPath, startupLogger);

            var jsonSeedPath = SeedJsonPath(app, config, "Courses");
            await CourseSeeder.SeedFromJsonAsync(db, jsonSeedPath, startupLogger);

            var assessmentJsonPath = SeedJsonPath(app, config, "Assessments");
            await AssessmentSeeder.SeedFromJsonAsync(db, assessmentJsonPath, startupLogger);

            var studentJsonPath = SeedJsonPath(app, config, "Students");
            var studentPassword = config["SeedData:StudentPassword"]
                ?? throw new InvalidOperationException(
                    "SeedData:StudentPassword is required when SeedData:Enabled=true. Set it via User Secrets.");
            await StudentSeeder.SeedFromJsonAsync(userManager, studentJsonPath, studentPassword, startupLogger);

            // Path steps name their courses by English title, so paths seed after the courses.
            var pathJsonPath = SeedJsonPath(app, config, "Paths");
            await LearningPathSeeder.SeedFromJsonAsync(db, pathJsonPath, startupLogger);

            // Testimonials and live classes; the admin account seeded above hosts the classes.
            var showcaseJsonPath = SeedJsonPath(app, config, "Showcase");
            var liveClassHost = await userManager.FindByEmailAsync(adminEmail)
                ?? throw new InvalidOperationException($"Admin user '{adminEmail}' not found; it hosts the seeded live classes.");
            await ShowcaseSeeder.SeedFromJsonAsync(db, showcaseJsonPath, liveClassHost.Id, TimeProvider.System, startupLogger);
        }
    }

    /// <summary>Whether startup seeds the sample content (programmes, exercises, members, …).</summary>
    public static bool SeedsSampleContent(IHostEnvironment environment, IConfiguration config) =>
        config.GetValue<bool>("SeedData:Enabled")
        && (environment.IsDevelopment() || config.GetValue<bool>("SeedData:AllowOutsideDevelopment"));

    /// <summary>
    /// The folder of one kind of sample JSON: SeedData:JsonPaths:{name} when set, else the published copy
    /// (SeedJson/, see FitEdu.Web.csproj), else the source tree's FitEdu.Shared/JSON folder.
    /// </summary>
    private static string SeedJsonPath(WebApplication app, IConfiguration config, string name)
    {
        var configured = config.GetValue<string>($"SeedData:JsonPaths:{name}");
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        var published = Path.Combine(app.Environment.ContentRootPath, "SeedJson", name);
        return Directory.Exists(published)
            ? published
            : Path.Combine(app.Environment.ContentRootPath, "..", "FitEdu.Shared", "JSON", name);
    }
}
