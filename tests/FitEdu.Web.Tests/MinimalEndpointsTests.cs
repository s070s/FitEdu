using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Identity;
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
