using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Infrastructure.Data;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AdminUsersIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AdminUsersIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetUsers_Admin_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsers_Admin_ReportsTheActiveTierOfAPlusMemberAndFreeForOthers()
    {
        var (plusMemberId, _) = await SignInNewUserAsync();
        var (freeMemberId, _) = await SignInNewUserAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var subscriptions = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
            var plusPlan = (await subscriptions.GetPlansAsync()).First(plan => plan.Tier == SubscriptionTier.Plus);
            await subscriptions.AssignPlanAsync(plusMemberId, plusPlan.Id);
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().SaveChangesAsync();
        }
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await ListedUserAsync(admin, plusMemberId)).SubscriptionTier.ShouldBe(SubscriptionTier.Plus);
        (await ListedUserAsync(admin, freeMemberId)).SubscriptionTier.ShouldBe(SubscriptionTier.Free);
    }

    // The user's row in the admin list, found by searching for their email (the shared test database holds many users).
    private static async Task<AdminUserDto> ListedUserAsync(HttpClient admin, string userId)
    {
        var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{userId}");
        var page = await admin.GetFromJsonAsync<PagedResult<AdminUserDto>>($"/api/admin/users?search={Uri.EscapeDataString(detail!.Email!)}");
        return page!.Items.Single(user => user.Id == userId);
    }

    [Fact]
    public async Task GetUsers_Student_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUser_Unknown_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await client.GetAsync("/api/admin/users/does-not-exist")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUser_Admin_ShowsTheTrainingGoal()
    {
        var (_, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await _factory.SeedAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Goal = FitnessGoal.FatLoss;
            await db.SaveChangesAsync();
        });
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{userId}");

        detail!.Goal.ShouldBe(nameof(FitnessGoal.FatLoss));
    }

    [Fact]
    public async Task GetRoles_Admin_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync("/api/admin/roles");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Student");
    }

    [Fact]
    public async Task DisableUser_Unknown_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsync("/api/admin/users/does-not-exist/disable", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>Creates a confirmed member (or another role), signs them in through the real login and returns their id and tokens.</summary>
    private async Task<(string UserId, AuthResponseDto Login)> SignInNewUserAsync(string role = "Student")
    {
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword, role);
        string? userId = null;
        await _factory.SeedAsync(async db => userId = (await db.Users.SingleAsync(u => u.Email == email)).Id);

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });
        var login = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        return (userId!, login);
    }

    private HttpClient ClientWithBearer(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto { RefreshToken = refreshToken });

    [Fact]
    public async Task DisableUser_Member_EndsTheirBearerAndRefreshTokensAndShowsDisabled()
    {
        var (memberId, login) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var member = ClientWithBearer(login.Token!);
        (await member.GetAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var disable = await admin.PostAsync($"/api/admin/users/{memberId}/disable", content: null);

        disable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{memberId}");
        detail!.IsEnabled.ShouldBeFalse();
        (await member.GetAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(_factory.CreateClient(), login.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnableUser_DisabledMember_CanSignInAgainAndShowsEnabled()
    {
        var (memberId, _) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        await admin.PostAsync($"/api/admin/users/{memberId}/disable", content: null);

        var enable = await admin.PostAsync($"/api/admin/users/{memberId}/enable", content: null);

        enable.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{memberId}");
        detail!.IsEnabled.ShouldBeTrue();
        var email = detail.Email!;
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EnableUser_MemberTheOldCodeLockedOut_SignsInAgain()
    {
        // The old Disable only set a lock-out end date far in the future; Enable must clear it too.
        var (memberId, _) = await SignInNewUserAsync();
        await _factory.SeedAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == memberId);
            user.IsEnabled = false;
            user.LockoutEnd = DateTimeOffset.MaxValue;
            await db.SaveChangesAsync();
        });
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        await admin.PostAsync($"/api/admin/users/{memberId}/enable", content: null);

        var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{memberId}");
        var login = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = detail!.Email!, Password = CustomWebAppFactory.TestPassword });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RemoveRole_Admin_EndsTheOtherAdminsSessionAtOnce()
    {
        var (otherAdminId, otherAdmin) = await SignInNewUserAsync("Admin");
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var otherClient = ClientWithBearer(otherAdmin.Token!);
        (await otherClient.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var removed = await admin.DeleteAsync($"/api/admin/users/{otherAdminId}/roles/Admin");

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await otherClient.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(_factory.CreateClient(), otherAdmin.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AssignRole_Member_EndsTheirOldSessionSoTheNextSignInCarriesTheNewRole()
    {
        var (memberId, login) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var memberClient = ClientWithBearer(login.Token!);

        var assigned = await admin.PostAsync($"/api/admin/users/{memberId}/roles/Admin", content: null);

        assigned.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await memberClient.GetAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(_factory.CreateClient(), login.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SetPassword_Member_TheNewPasswordWorksAndEveryOldSessionEnds()
    {
        const string newPassword = "Admin-Set-Pass-1!";
        var (memberId, login) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var email = (await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{memberId}"))!.Email!;

        var response = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/set-password", new AdminSetPasswordDto { NewPassword = newPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ClientWithBearer(login.Token!).GetAsync("/api/profile")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(_factory.CreateClient(), login.RefreshToken!)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var oldPasswordLogin = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });
        var newPasswordLogin = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = newPassword });
        oldPasswordLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        newPasswordLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SetPassword_AdminAccount_Returns400()
    {
        var (otherAdminId, _) = await SignInNewUserAsync("Admin");
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await admin.PostAsJsonAsync($"/api/admin/users/{otherAdminId}/set-password", new AdminSetPasswordDto { NewPassword = "Admin-Set-Pass-1!" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SetPassword_LongerThanTheMaximum_Returns400()
    {
        var (memberId, _) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var tooLong = "Aa1!" + new string('x', PasswordRules.MaximumLength - 3);

        var response = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/set-password", new AdminSetPasswordDto { NewPassword = tooLong });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void ImpersonateEndpoint_UsesTheAuthRateLimit()
    {
        var endpoint = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == "api/admin/users/{userId}/impersonate");

        var policyName = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;

        policyName.ShouldBe("auth");
    }

    [Fact]
    public async Task Impersonate_DisabledMember_Returns400()
    {
        var (memberId, _) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        await admin.PostAsync($"/api/admin/users/{memberId}/disable", content: null);

        var response = await admin.PostAsync($"/api/admin/users/{memberId}/impersonate", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Impersonate_EnabledMember_ReturnsAShortLivedTokenWithoutARefreshToken()
    {
        var (memberId, _) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await admin.PostAsync($"/api/admin/users/{memberId}/impersonate", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Token.ShouldNotBeNullOrEmpty();
        body.RefreshToken.ShouldBeNull();
    }

    [Fact]
    public async Task DisableUser_AdminAccount_IsRefusedAndTheAdminKeepsWorking()
    {
        var (otherAdminId, otherAdmin) = await SignInNewUserAsync("Admin");
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        var disable = await admin.PostAsync($"/api/admin/users/{otherAdminId}/disable", content: null);

        disable.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ClientWithBearer(otherAdmin.Token!).GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
