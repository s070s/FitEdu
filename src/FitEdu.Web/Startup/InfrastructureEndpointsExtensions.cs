using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Services;
using FitEdu.Web.Pages;
using FitEdu.Web.Services;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace FitEdu.Web.Startup;

/// <summary>
/// Minimal-API endpoints tagged "Infrastructure": culture switching, the Blazor auth-completion
/// endpoints (sign-in, leaving an impersonation, sign-out), the XML sitemap and the health probes.
/// Grouped together because none of them are JSON APIs — they're browser-navigation, crawler or
/// monitoring endpoints.
/// </summary>
public static class InfrastructureEndpointsExtensions
{
    public static WebApplication MapInfrastructureEndpoints(this WebApplication app)
    {
        // --- Culture endpoint ---
        app.MapGet("/culture/set", (string? culture, string? returnUrl, HttpContext ctx, IOptions<RequestLocalizationOptions> localization) =>
        {
            ctx.Response.Cookies.Append(
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.DefaultCookieName,
                Microsoft.AspNetCore.Localization.CookieRequestCultureProvider.MakeCookieValue(
                    new Microsoft.AspNetCore.Localization.RequestCulture(SupportedCultureName(culture, localization.Value))),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true });

            return ToLocalRedirect(returnUrl);
        })
        .WithTags("Infrastructure")
        .WithName("SetCulture")
        .WithSummary("Set the UI culture cookie and redirect back.")
        .WithDescription("Writes the ASP.NET Core localization cookie (supported cultures: en-GB, el-GR; any other value stores the default) and 302-redirects to a local returnUrl. Used by the language switcher; browser-navigation endpoint, not a JSON API.")
        .Produces(StatusCodes.Status302Found);

        // --- Auth completion endpoints ---
        // Blazor Server circuits run after the HTTP response has already been committed, so
        // cookie operations (SignInAsync / SignOutAsync) must happen on a separate fresh request.
        // Components issue NavigationManager.NavigateTo(url, forceLoad: true) with a signed
        // DataProtection token.  These endpoints decode the token, do a single DB lookup to
        // rebuild the ClaimsPrincipal, then call SignInAsync / SignOutAsync normally.

        app.MapGet("/auth/complete", async (
            string ticket,
            string? returnUrl,
            HttpContext ctx,
            IDataProtectionProvider dpProvider,
            UserManager<ApplicationUser> userManager,
            IMemoryCache ticketCache,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthCompletion");

            // --- Decode the signed token -------------------------------------------------
            string payload;
            try
            {
                var protector = dpProvider
                    .CreateProtector(AuthService.ProtectorPurpose)
                    .ToTimeLimitedDataProtector();
                payload = protector.Unprotect(ticket);
            }
            catch (Exception ex)
            {
                StartupLog.AuthTokenInvalid(logger, ex);
                return Results.LocalRedirect("/login?error=session_expired");
            }

            AuthCompletionTicket? decoded;
            try
            {
                decoded = JsonSerializer.Deserialize<AuthCompletionTicket>(payload);
            }
            catch (JsonException ex)
            {
                StartupLog.AuthTokenBadFormat(logger, ex);
                return Results.LocalRedirect("/login?error=session_expired");
            }
            if (decoded is null)
            {
                StartupLog.AuthTokenEmpty(logger);
                return Results.LocalRedirect("/login?error=session_expired");
            }

            // Each ticket opens once: a copy of the link (a login CSRF) finds it spent.
            if (!TryUseTicket(ticketCache, decoded.TicketId))
            {
                StartupLog.AuthTicketReused(logger);
                return Results.LocalRedirect("/login?error=session_expired");
            }

            var userId = decoded.UserId;
            var adminBackupId = string.IsNullOrEmpty(decoded.AdminBackupId) ? null : decoded.AdminBackupId;
            var tokenStamp = decoded.SecurityStamp;
            var rememberMe = decoded.RememberMe;

            // --- Rebuild principal from DB -----------------------------------------------
            var user = await userManager.FindByIdAsync(userId);
            if (user is null)
            {
                StartupLog.AuthUserNotFound(logger, userId);
                return Results.LocalRedirect("/login");
            }

            // Reject if the user changed their password/security stamp since the token was issued.
            if (user.SecurityStamp != tokenStamp)
            {
                StartupLog.AuthStampMismatch(logger, userId);
                return Results.LocalRedirect("/login?error=session_expired");
            }

            // A disabled account gets no session, even with a ticket issued before it was disabled.
            if (!user.IsEnabled)
            {
                StartupLog.AuthUserDisabled(logger, userId);
                return Results.LocalRedirect("/login");
            }

            var principal = await BuildPrincipalAsync(user, adminBackupId, userManager);

            // --- Write / clear admin backup cookie ---------------------------------------
            // Session-only, like the impersonated session it leads back from. A plain sign-in deletes
            // any backup cookie left behind, so it can never turn a later session into an exit.
            if (!string.IsNullOrEmpty(adminBackupId))
            {
                var adminCookieProtector = dpProvider.CreateProtector(
                    AuthService.AdminBackupCookieProtectorPurpose);
                ctx.Response.Cookies.Append(
                    AuthService.AdminBackupCookieName,
                    adminCookieProtector.Protect(adminBackupId),
                    new CookieOptions
                    {
                        HttpOnly = true,
                        SameSite = SameSiteMode.Strict,
                        Secure = !app.Environment.IsDevelopment(),
                        IsEssential = true
                    });
            }
            else
            {
                ctx.Response.Cookies.Delete(AuthService.AdminBackupCookieName);
            }

            // --- Issue auth cookie -------------------------------------------------------
            // IsPersistent=false (unchecked "Remember Me") issues a session cookie the browser
            // discards on close; the cookie middleware's 24h sliding ExpireTimeSpan still governs
            // the ticket while the session is open. IsPersistent=true makes it a 7-day cookie that
            // is renewed while the member stays active (sliding expiration).
            var authProperties = new AuthenticationProperties { IsPersistent = rememberMe };
            if (rememberMe)
                authProperties.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7);

            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                authProperties);

            StartupLog.AuthSignedIn(logger, userId);
            if (!string.IsNullOrEmpty(adminBackupId))
                StartupLog.AuthImpersonating(logger, adminBackupId, userId);

            return ToLocalRedirect(returnUrl);
        }).AllowAnonymous()
        .WithTags("Infrastructure")
        .WithName("CompleteAuth")
        .WithSummary("Complete Blazor sign-in and issue the auth cookie.")
        .WithDescription("Decodes a signed DataProtection ticket, rebuilds the principal from the database, and issues the .FitEdu.Auth cookie (and admin-backup cookie when impersonating). Invoked via full-page redirect from Blazor after login. Returns a 302 redirect; browser-navigation endpoint, not a JSON API.")
        .Produces(StatusCodes.Status302Found);

        // Leaving an impersonation. Trusts the session's impersonatedBy claim only together with the
        // backup cookie naming the same admin, and signs that admin back in only while they are still an
        // enabled Admin; a bare backup cookie on a plain session does nothing but get deleted.
        app.MapGet(ExitImpersonationPath, async (
            HttpContext ctx,
            IDataProtectionProvider dpProvider,
            UserManager<ApplicationUser> userManager,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("AuthCompletion");
            var impersonatedBy = ctx.User.FindFirstValue(UserClaimsBuilder.ImpersonatedByClaim);
            var rawBackupCookie = ctx.Request.Cookies[AuthService.AdminBackupCookieName];

            // The backup cookie is spent whatever happens next.
            ctx.Response.Cookies.Delete(AuthService.AdminBackupCookieName);

            if (string.IsNullOrEmpty(impersonatedBy))
            {
                StartupLog.AuthExitRefused(logger, "the session is not an impersonation");
                return Results.LocalRedirect("/");
            }

            var admin = await FindAdminToRestoreAsync(rawBackupCookie, impersonatedBy, dpProvider, userManager, logger);
            if (admin is null)
            {
                await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.LocalRedirect("/");
            }

            var principal = await BuildPrincipalAsync(admin, adminBackupId: null, userManager);
            await ctx.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = false });

            StartupLog.AuthExitedImpersonation(logger, admin.Id);
            return Results.LocalRedirect("/admin/users");
        }).AllowAnonymous()
        .WithTags("Infrastructure")
        .WithName("ExitImpersonation")
        .WithSummary("Leave an impersonation and sign the admin back in.")
        .WithDescription("Checks the impersonatedBy claim against the signed admin-backup cookie, then signs the admin back in for the browser session and deletes the cookie. Any mismatch signs the browser out instead. Browser-navigation endpoint, not a JSON API.")
        .Produces(StatusCodes.Status302Found);

        app.MapGet("/auth/signout", async (string? returnUrl, HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            ctx.Response.Cookies.Delete(AuthService.AdminBackupCookieName);

            return ToLocalRedirect(returnUrl);
        }).AllowAnonymous()
        .WithTags("Infrastructure")
        .WithName("SignOut")
        .WithSummary("Clear the auth cookie and redirect.")
        .WithDescription("Signs out the cookie session and deletes the admin-backup cookie, then 302-redirects to a local returnUrl. Browser-navigation endpoint, not a JSON API.")
        .Produces(StatusCodes.Status302Found);

        // --- Sitemap ---
        app.MapGet("/sitemap.xml", async (IBlogArticleService blog, IExerciseService exercises, ILearningPathService paths,
            ICourseService courses, IConfiguration sitemapConfig, IMemoryCache sitemapCache, HttpContext ctx) =>
        {
            var baseUrl = sitemapConfig[SitemapBuilder.BaseUrlSetting]
                ?? throw new InvalidOperationException($"{SitemapBuilder.BaseUrlSetting} is not configured.");

            // Cache the rendered XML for 30 minutes — avoids a DB hit + StringBuilder allocation on every crawler request.
            var xml = await sitemapCache.GetOrCreateAsync("sitemap.xml", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);
                var articles = await blog.GetPublishedSummariesAsync(200, "en", ctx.RequestAborted);
                var exerciseSlugs = await exercises.GetPublishedSlugsAsync(ctx.RequestAborted);
                var publishedPaths = await paths.GetPublishedAsync(null, "en", ctx.RequestAborted);
                var courseIds = await courses.GetPublishedCourseIdsAsync(ctx.RequestAborted);

                var sitemap = new SitemapBuilder(baseUrl);
                sitemap.AddPage("/", "1.0", "weekly");

                // The pages anyone can open without signing in (robots.txt keeps the rest out).
                sitemap.AddPage("/courses", "0.8", "weekly");
                sitemap.AddPage("/exercises", "0.8", "weekly");
                sitemap.AddPage("/paths", "0.8", "weekly");
                sitemap.AddPage("/blog", "0.7", "weekly");
                sitemap.AddPage("/pricing", "0.6", "monthly");
                sitemap.AddPage("/privacy", "0.3", "yearly");
                sitemap.AddPage("/terms", "0.3", "yearly");

                foreach (var article in articles)
                    sitemap.AddPage($"/blog/{article.Slug}", "0.8", "monthly", article.PublishedAt ?? DateTimeOffset.UtcNow);

                foreach (var courseId in courseIds)
                    sitemap.AddPage($"/courses/{courseId}", "0.7", "monthly");

                foreach (var path in publishedPaths)
                    sitemap.AddPage($"/paths/{path.Id}", "0.7", "monthly");

                foreach (var exerciseSlug in exerciseSlugs)
                    sitemap.AddPage($"/exercises/{exerciseSlug}", "0.6", "monthly");

                return sitemap.ToXml();
            });

            ctx.Response.ContentType = "application/xml; charset=utf-8";
            await ctx.Response.WriteAsync(xml!);
        }).AllowAnonymous()
        .WithTags("Infrastructure")
        .WithName("Sitemap")
        .WithSummary("XML sitemap of public pages, published blog articles, programmes, learning paths and exercises.")
        .WithDescription("Returns an application/xml sitemap for search-engine crawlers. Cached for 30 minutes.")
        .Produces(StatusCodes.Status200OK, contentType: "application/xml");

        // --- Health checks (AVAIL-1) ---
        // Liveness: is the process itself responsive? No dependency checks (Predicate: false runs
        // none of the registered checks) — a slow/unreachable DB should fail readiness, not liveness.
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .AllowAnonymous()
            .WithTags("Infrastructure")
            .WithName("HealthLive")
            .WithSummary("Liveness probe — process is up, no dependency checks.")
            .ExcludeFromDescription();

        // Readiness: can this instance actually serve traffic? Runs every check tagged "ready"
        // (database, assistant). Healthy/Degraded both return 200 by default; only Unhealthy (the
        // database check) returns 503 — the assistant being degraded doesn't block readiness.
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
            {
                Predicate = check => check.Tags.Contains("ready")
            })
            .AllowAnonymous()
            .WithTags("Infrastructure")
            .WithName("HealthReady")
            .WithSummary("Readiness probe — database + assistant status.")
            .ExcludeFromDescription();

        return app;
    }

    /// <summary>
    /// Redirects to the return address when it is a path on this site, otherwise to the home page.
    /// NavigationManager.Uri passes an absolute URL, so its path and query are taken first. "//host"
    /// and "/\host" (also what "https://x//host" turns into) fall back to the home page, as does
    /// anything with a control character, so <c>Results.LocalRedirect</c> never throws. A value that starts
    /// with "/" is never parsed as an absolute URL: on Linux "/courses/42" parses as a file: URI.
    /// </summary>
    internal static IResult ToLocalRedirect(string? returnUrl)
    {
        var path = returnUrl;
        if (returnUrl is not null && !returnUrl.StartsWith('/') && Uri.TryCreate(returnUrl, UriKind.Absolute, out var absolute))
            path = absolute.Scheme is "http" or "https" ? absolute.PathAndQuery : null;

        var localPath = Login.LocalReturnPath(path);
        return Results.LocalRedirect(localPath.Any(char.IsControl) ? "/" : localPath);
    }

    /// <summary>Where the impersonation banner sends the admin to leave an impersonated session.</summary>
    public const string ExitImpersonationPath = "/auth/exit-impersonation";

    /// <summary>The cookie principal for a user: identity, roles and (when impersonating) the admin's id.</summary>
    private static async Task<ClaimsPrincipal> BuildPrincipalAsync(
        ApplicationUser user,
        string? adminBackupId,
        UserManager<ApplicationUser> userManager)
    {
        var roles = await userManager.GetRolesAsync(user);
        var claims = UserClaimsBuilder.Build(user, roles, adminBackupId);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// The admin named by the backup cookie, or null (with the reason logged) when the cookie is missing,
    /// altered, names someone other than the session's impersonatedBy admin, or the admin is gone, disabled
    /// or no longer an Admin.
    /// </summary>
    private static async Task<ApplicationUser?> FindAdminToRestoreAsync(
        string? rawBackupCookie,
        string impersonatedBy,
        IDataProtectionProvider dpProvider,
        UserManager<ApplicationUser> userManager,
        ILogger logger)
    {
        if (string.IsNullOrEmpty(rawBackupCookie))
        {
            StartupLog.AuthExitRefused(logger, "the admin backup cookie is missing");
            return null;
        }

        string adminIdFromCookie;
        try
        {
            adminIdFromCookie = dpProvider.CreateProtector(AuthService.AdminBackupCookieProtectorPurpose).Unprotect(rawBackupCookie);
        }
        catch (Exception)
        {
            StartupLog.AuthExitRefused(logger, "the admin backup cookie failed its integrity check");
            return null;
        }

        if (adminIdFromCookie != impersonatedBy)
        {
            StartupLog.AuthExitRefused(logger, "the admin backup cookie names another admin than the session");
            return null;
        }

        var admin = await userManager.FindByIdAsync(adminIdFromCookie);
        if (admin is null || !admin.IsEnabled || !await userManager.IsInRoleAsync(admin, "Admin"))
        {
            StartupLog.AuthExitRefused(logger, "the admin is gone, disabled or no longer an Admin");
            return null;
        }

        return admin;
    }

    // The ticket ids already opened. A used id is remembered a little longer than a ticket lives, so it is
    // spent for as long as the ticket could still be decoded. One lock makes check-and-remember a single step.
    private static readonly object TicketUseLock = new();

    /// <summary>True the first time the ticket id is seen; false for an empty id or one already used.</summary>
    private static bool TryUseTicket(IMemoryCache cache, Guid ticketId)
    {
        if (ticketId == Guid.Empty)
            return false;

        var cacheKey = $"auth-ticket-used:{ticketId}";
        lock (TicketUseLock)
        {
            if (cache.TryGetValue(cacheKey, out _))
                return false;

            cache.Set(cacheKey, true, AuthService.SignInTicketLifetime + TimeSpan.FromMinutes(1));
            return true;
        }
    }

    /// <summary>The supported UI culture the requested name stands for (any spelling), else the default one.</summary>
    private static string SupportedCultureName(string? culture, RequestLocalizationOptions options)
    {
        var supported = options.SupportedUICultures?.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, culture, StringComparison.OrdinalIgnoreCase));
        return (supported ?? options.DefaultRequestCulture.UICulture).Name;
    }
}
