using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class MinimalEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public MinimalEndpointsTests(CustomWebAppFactory factory) => _factory = factory;

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthComplete_SetCookie_MaxAgeMatchesRememberMe(bool rememberMe)
    {
        var email = $"remember-me-{Guid.NewGuid():N}@test.com";
        var password = "Test-Pass-1!";
        await _factory.CreateConfirmedUserAsync(email, password);

        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var loginResult = await authService.LoginAsync(new LoginRequestDto
        {
            Email = email,
            Password = password,
            RememberMe = rememberMe
        });
        loginResult.Success.ShouldBeTrue();

        var client = NoRedirectClient();
        var response = await client.GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(loginResult.Token!)}&returnUrl=%2F");

        response.Headers.TryGetValues("Set-Cookie", out var setCookieValues).ShouldBeTrue();
        var authCookie = setCookieValues!.FirstOrDefault(v => v.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
        authCookie.ShouldNotBeNull();

        // rememberMe=false must yield a session cookie (no expires/max-age) so it's discarded
        // when the browser closes; rememberMe=true must yield a persistent cookie.
        var hasExpiry = authCookie.Contains("expires=", StringComparison.OrdinalIgnoreCase)
            || authCookie.Contains("max-age=", StringComparison.OrdinalIgnoreCase);
        hasExpiry.ShouldBe(rememberMe, $"Set-Cookie was: {authCookie}");
    }

    [Fact]
    public async Task Sitemap_ListsThePublicPagesAndEachPublishedPathAndProgramme()
    {
        // The only sitemap request in this class (the XML is cached for 30 minutes), so the paths
        // and programmes seeded here are always in it.
        var published = new LearningPath { TitleEn = "Sitemap Beginner Start", IsPublished = true };
        var draft = new LearningPath { TitleEn = "Sitemap Draft Path", IsPublished = false };
        var publishedCourse = new Course { Id = Guid.NewGuid(), TitleEn = "Sitemap Programme", IsPublished = true };
        var draftCourse = new Course { Id = Guid.NewGuid(), TitleEn = "Sitemap Draft Programme", IsPublished = false };
        await _factory.SeedAsync(async db =>
        {
            db.LearningPaths.AddRange(published, draft);
            db.Courses.AddRange(publishedCourse, draftCourse);
            await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/sitemap.xml");
        var xml = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/xml");
        string[] publicPages = ["/", "/courses", "/exercises", "/paths", "/blog", "/pricing", "/privacy", "/terms"];
        foreach (var page in publicPages)
            xml.ShouldContain($"<loc>https://tests.local{page}</loc>", Case.Sensitive);
        xml.ShouldContain($"<loc>https://tests.local/paths/{published.Id}</loc>", Case.Sensitive);
        xml.ShouldNotContain(draft.Id.ToString());
        xml.ShouldContain($"<loc>https://tests.local/courses/{publishedCourse.Id}</loc>", Case.Sensitive);
        xml.ShouldNotContain(draftCourse.Id.ToString());
    }

    [Fact]
    public async Task AuthComplete_InvalidTicket_RedirectsToLogin()
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync("/auth/complete?ticket=garbage&returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldContain("/login");
    }

    [Fact]
    public async Task Signout_RedirectsLocally()
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync("/auth/signout?returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task CultureSet_RedirectsLocally()
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync("/culture/set?culture=el-GR&returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
    }

    // A return address that points to another site, or only looks local, is replaced by the home page;
    // an absolute address keeps its path and query (the redirect stays on this site).
    public static TheoryData<string, string> ReturnUrlCases => new()
    {
        { "https://x//evil.com", "/" },
        { "//evil.com", "/" },
        { "/\\evil.com", "/" },
        { "https://evil.com/p?q=1", "/p?q=1" },
        { "/courses/42", "/courses/42" }
    };

    [Theory]
    [MemberData(nameof(ReturnUrlCases))]
    public async Task CultureSet_ReturnUrl_RedirectsOnlyToALocalPath(string returnUrl, string expectedLocation)
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync($"/culture/set?culture=el-GR&returnUrl={Uri.EscapeDataString(returnUrl)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe(expectedLocation);
    }

    [Theory]
    [MemberData(nameof(ReturnUrlCases))]
    public async Task Signout_ReturnUrl_RedirectsOnlyToALocalPath(string returnUrl, string expectedLocation)
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync($"/auth/signout?returnUrl={Uri.EscapeDataString(returnUrl)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe(expectedLocation);
    }

    [Theory]
    [MemberData(nameof(ReturnUrlCases))]
    public async Task AuthComplete_ReturnUrl_RedirectsOnlyToALocalPathAndKeepsTheSignIn(string returnUrl, string expectedLocation)
    {
        var ticket = await SignInTicketAsync();
        var client = NoRedirectClient();

        var response = await client.GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl={Uri.EscapeDataString(returnUrl)}");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe(expectedLocation);
        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthComplete_Impersonation_BackupCookieIsStrictAndHttpOnly()
    {
        var ticket = await CraftedTicketAsync(adminBackupId: "admin-1");

        var response = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F");

        var backupCookie = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".FitEdu.AdminUserId=", StringComparison.Ordinal));
        backupCookie.ShouldContain("samesite=strict", Case.Insensitive);
        backupCookie.ShouldContain("httponly", Case.Insensitive);
    }

    /// <summary>
    /// A sign-in ticket for a fresh confirmed member, built the way the sign-in page's service builds it
    /// but with the impersonation fields chosen by the test.
    /// </summary>
    private async Task<string> CraftedTicketAsync(string? adminBackupId = null, bool disableMember = false)
    {
        var email = $"crafted-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);

        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = (await userManager.FindByEmailAsync(email))!;
        if (disableMember)
        {
            member.IsEnabled = false;
            await userManager.UpdateAsync(member);
        }
        var ticket = new AuthCompletionTicket(member.Id, adminBackupId, member.SecurityStamp!, RememberMe: false, Guid.NewGuid());
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(AuthService.ProtectorPurpose)
            .ToTimeLimitedDataProtector();
        return protector.Protect(JsonSerializer.Serialize(ticket), AuthService.SignInTicketLifetime);
    }

    [Fact]
    public async Task AuthComplete_Impersonation_BackupCookieEndsWithTheBrowserSession()
    {
        var ticket = await CraftedTicketAsync(adminBackupId: "admin-1");

        var response = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F");

        var backupCookie = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".FitEdu.AdminUserId=", StringComparison.Ordinal));
        backupCookie.ShouldNotContain("expires=", Case.Insensitive);
        backupCookie.ShouldNotContain("max-age=", Case.Insensitive);
    }

    [Fact]
    public async Task AuthComplete_PlainLogin_DeletesAdminBackupCookie()
    {
        // A backup cookie left behind (a closed tab, an old impersonation) must not outlive a normal sign-in.
        var staleBackupCookie = ProtectedBackupCookie("admin-1");
        var ticket = await SignInTicketAsync();

        var response = await GetWithCookiesAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F", staleBackupCookie);

        response.Headers.GetValues("Set-Cookie").ShouldContain(cookie => IsDeletedBackupCookie(cookie));
    }

    [Fact]
    public async Task AuthComplete_DisabledUser_RedirectsToLoginWithoutAuthCookie()
    {
        var ticket = await CraftedTicketAsync(disableMember: true);

        var response = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().ShouldBe("/login");
        response.Headers.TryGetValues("Set-Cookie", out var cookies);
        (cookies ?? []).ShouldNotContain(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExitImpersonation_AdminImpersonating_SignsTheAdminBackInForTheSessionOnly()
    {
        var (adminId, adminEmail) = await CreateAdminAsync();
        var ticket = await CraftedTicketAsync(adminBackupId: adminId);
        var completed = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F");

        var exit = await GetWithCookiesAsync("/auth/exit-impersonation", CookieHeader(completed));

        exit.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        exit.Headers.Location!.ToString().ShouldBe("/admin/users");
        var adminCookie = exit.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
        adminCookie.ShouldNotContain("expires=", Case.Insensitive);
        exit.Headers.GetValues("Set-Cookie").ShouldContain(cookie => IsDeletedBackupCookie(cookie));
        var me = await GetWithCookiesAsync("/api/auth/me", CookieHeader(exit));
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await me.Content.ReadAsStringAsync()).ShouldContain(adminEmail);
    }

    [Fact]
    public async Task ExitImpersonation_WithoutImpersonatedByClaim_DoesNotSignInAdmin()
    {
        // Only the backup cookie names an admin here; the signed-in session is a plain member's, so
        // the cookie alone must not turn that browser into the admin.
        var (adminId, _) = await CreateAdminAsync();
        var plainSignIn = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(await SignInTicketAsync())}&returnUrl=%2F");

        var exit = await GetWithCookiesAsync("/auth/exit-impersonation", CookieHeader(plainSignIn), ProtectedBackupCookie(adminId));

        exit.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        exit.Headers.GetValues("Set-Cookie").ShouldNotContain(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
        exit.Headers.GetValues("Set-Cookie").ShouldContain(cookie => IsDeletedBackupCookie(cookie));
    }

    [Fact]
    public async Task ExitImpersonation_BackupCookieNamesAnotherAdmin_SignsOutInsteadOfSigningIn()
    {
        var (adminId, _) = await CreateAdminAsync();
        var (otherAdminId, _) = await CreateAdminAsync();
        var ticket = await CraftedTicketAsync(adminBackupId: adminId);
        var completed = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F");

        // The browser keeps the impersonated session but carries a backup cookie naming a different admin.
        var exit = await GetWithCookiesAsync("/auth/exit-impersonation", AuthCookieOnly(completed), ProtectedBackupCookie(otherAdminId));

        exit.Headers.GetValues("Set-Cookie").Where(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal))
            .ShouldAllBe(cookie => cookie.StartsWith(".FitEdu.Auth=;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExitImpersonation_AdminNoLongerEnabled_SignsOutInsteadOfSigningIn()
    {
        var (adminId, _) = await CreateAdminAsync();
        var ticket = await CraftedTicketAsync(adminBackupId: adminId);
        var completed = await NoRedirectClient().GetAsync($"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F");
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = (await userManager.FindByIdAsync(adminId))!;
            admin.IsEnabled = false;
            await userManager.UpdateAsync(admin);
        }

        var exit = await GetWithCookiesAsync("/auth/exit-impersonation", CookieHeader(completed));

        exit.Headers.GetValues("Set-Cookie").Where(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal))
            .ShouldAllBe(cookie => cookie.StartsWith(".FitEdu.Auth=;", StringComparison.Ordinal));
    }

    private static bool IsDeletedBackupCookie(string cookie) =>
        cookie.StartsWith(".FitEdu.AdminUserId=;", StringComparison.Ordinal);

    /// <summary>A backup cookie (name=value) naming the given admin, as /auth/complete writes it.</summary>
    private string ProtectedBackupCookie(string adminId)
    {
        var protector = _factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(AuthService.AdminBackupCookieProtectorPurpose);
        return $"{AuthService.AdminBackupCookieName}={Uri.EscapeDataString(protector.Protect(adminId))}";
    }

    private async Task<(string Id, string Email)> CreateAdminAsync()
    {
        var email = $"admin-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword, role: "Admin");
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return ((await userManager.FindByEmailAsync(email))!.Id, email);
    }

    /// <summary>The cookies a browser keeps from a response: every Set-Cookie that is not a deletion, as name=value pairs.</summary>
    private static string CookieHeader(HttpResponseMessage response) =>
        string.Join("; ", response.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';')[0])
            .Where(pair => !pair.EndsWith('='))); // a deletion is written as name=

    private static string AuthCookieOnly(HttpResponseMessage response) =>
        string.Join("; ", CookieHeader(response).Split("; ").Where(pair => pair.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal)));

    private async Task<HttpResponseMessage> GetWithCookiesAsync(string url, params string[] cookiePairs)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", string.Join("; ", cookiePairs));
        return await NoRedirectClient().SendAsync(request);
    }

    [Fact]
    public async Task AuthComplete_SameTicketTwice_SecondRedirectsToLoginWithoutCookie()
    {
        // A ticket opened once is spent: a copy of the link cannot sign anyone else's browser in.
        var ticket = await SignInTicketAsync();
        var url = $"/auth/complete?ticket={Uri.EscapeDataString(ticket)}&returnUrl=%2F";

        var first = await NoRedirectClient().GetAsync(url);
        var second = await NoRedirectClient().GetAsync(url);

        first.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith(".FitEdu.Auth=", StringComparison.Ordinal));
        second.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        second.Headers.Location!.ToString().ShouldBe("/login?error=session_expired");
        second.Headers.TryGetValues("Set-Cookie", out var secondCookies).ShouldBeFalse(string.Join("; ", secondCookies ?? []));
    }

    [Theory]
    [InlineData("zz!!")]
    [InlineData("fr-FR")]
    [InlineData("")]
    public async Task CultureSet_MalformedOrUnsupportedCulture_Redirects302AndSetsTheDefaultLanguage(string culture)
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync($"/culture/set?culture={Uri.EscapeDataString(culture)}&returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cultureCookie = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal));
        Uri.UnescapeDataString(cultureCookie).ShouldContain("c=en-GB|uic=en-GB");
    }

    [Fact]
    public async Task CultureSet_SupportedCulture_IsStoredInItsOwnSpelling()
    {
        var client = NoRedirectClient();

        var response = await client.GetAsync("/culture/set?culture=EL-gr&returnUrl=%2F");

        var cultureCookie = response.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith(".AspNetCore.Culture=", StringComparison.Ordinal));
        Uri.UnescapeDataString(cultureCookie).ShouldContain("c=el-GR|uic=el-GR");
    }

    /// <summary>A valid sign-in ticket for a fresh confirmed member, as the sign-in page hands to /auth/complete.</summary>
    private async Task<string> SignInTicketAsync()
    {
        var email = $"ticket-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);

        using var scope = _factory.Services.CreateScope();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var loginResult = await authService.LoginAsync(new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });
        loginResult.Success.ShouldBeTrue();
        return loginResult.Token!;
    }

    [Fact]
    public async Task CultureSet_SignedInMember_KeepsTheStoredLanguage()
    {
        // The switch is a plain GET link any site could send a member to, so it changes only the cookie.
        var (signedInClient, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await SetStoredLanguageAsync(userId, UserLanguage.English);
        var client = NoRedirectClient();
        client.DefaultRequestHeaders.Authorization = signedInClient.DefaultRequestHeaders.Authorization;

        var response = await client.GetAsync("/culture/set?culture=el-GR&returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StoredLanguageAsync(userId)).ShouldBe(UserLanguage.English);
    }

    [Fact]
    public async Task CultureSet_AdminImpersonatingAMember_KeepsTheMembersStoredLanguage()
    {
        var (_, memberId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await SetStoredLanguageAsync(memberId, UserLanguage.Greek);
        var client = NoRedirectClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await ImpersonationTokenAsync(memberId));

        var response = await client.GetAsync("/culture/set?culture=en-GB&returnUrl=%2F");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await StoredLanguageAsync(memberId)).ShouldBe(UserLanguage.Greek);
    }

    /// <summary>A token for the member that carries the impersonatedBy claim of an admin acting as them.</summary>
    private async Task<string> ImpersonationTokenAsync(string memberId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = await userManager.FindByIdAsync(memberId);
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var (token, _) = await tokenService.GenerateImpersonationTokenAsync(member!, adminId: "admin-1");
        return token;
    }

    private async Task SetStoredLanguageAsync(string userId, string language)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        user!.PreferredLanguage = language;
        await userManager.UpdateAsync(user);
    }

    private async Task<string?> StoredLanguageAsync(string userId)
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        return user?.PreferredLanguage;
    }

    [Fact]
    public async Task HealthLive_AnonymousReturns200_WithNoDependencyChecks()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthReady_AnonymousReturns200_AgainstTheTestDatabase()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
