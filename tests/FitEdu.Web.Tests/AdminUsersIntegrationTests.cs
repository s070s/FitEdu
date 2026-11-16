using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
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
    public async Task GetUsers_Admin_IncludesSubscriptionTier()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync("/api/admin/users");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("subscriptionTier");
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
    public async Task SetPassword_LongerThanTheMaximum_Returns400()
    {
        var (memberId, _) = await SignInNewUserAsync();
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var tooLong = "Aa1!" + new string('x', PasswordRules.MaximumLength - 3);

        var response = await admin.PostAsJsonAsync($"/api/admin/users/{memberId}/set-password", new AdminSetPasswordDto { NewPassword = tooLong });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
