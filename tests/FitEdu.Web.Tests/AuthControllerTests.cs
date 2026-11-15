using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AuthControllerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AuthControllerTests(CustomWebAppFactory factory) => _factory = factory;

    private static RegisterRequestDto Register(string email) => new()
    {
        Email = email,
        Password = "Password1!",
        ConfirmPassword = "Password1!",
        FirstName = "New",
        LastName = "User",
        GdprConsent = true
    };

    [Fact]
    public async Task Register_Valid_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", Register($"reg-{Guid.NewGuid():N}@test.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Register_EmailWithApostrophe_Returns200()
    {
        var client = _factory.CreateClient();

        // The user name is the email, so Identity's default user-name character list must not reject it.
        var response = await client.PostAsJsonAsync("/api/auth/register", Register($"o'brien-{Guid.NewGuid():N}@test.com"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Register_CommonPassword_Returns400()
    {
        var client = _factory.CreateClient();
        var request = Register($"common-{Guid.NewGuid():N}@test.com");
        // Passes the length, capital and digit rules, but is one of the most common passwords.
        request.Password = "Password123";
        request.ConfirmPassword = "Password123";

        var response = await client.PostAsJsonAsync("/api/auth/register", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Errors!.ShouldContain(ErrorMessagesRes.IdentityPasswordTooCommon);
    }

    [Fact]
    public void IdentityPasswordOptions_UseTheSharedMinimumLength()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.ShouldBe(PasswordRules.MinimumLength);
    }

    [Fact]
    public async Task Register_InvalidModel_Returns400()
    {
        var client = _factory.CreateClient();
        var invalid = Register("not-an-email");
        invalid.GdprConsent = false;

        var response = await client.PostAsJsonAsync("/api/auth/register", invalid);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns400Generic()
    {
        var email = $"dup-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", Register(email));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Errors.ShouldNotBeNull();
        // Account-enumeration safe: duplicate is masked, never says "email already exists".
        string.Join(" ", body.Errors!).ShouldNotContain("already");
    }

    // ---- Confirm email: POST only, the emailed link opens the site's page which posts here ----

    private async Task<(string UserId, string Token)> CreateUnconfirmedUserWithTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"confirm-{Guid.NewGuid():N}@test.com";
        var user = new ApplicationUser { UserName = email, Email = email, FirstName = "Test", LastName = "User" };
        (await userManager.CreateAsync(user, CustomWebAppFactory.TestPassword)).Succeeded.ShouldBeTrue();
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        return (user.Id, token);
    }

    [Fact]
    public async Task ConfirmEmail_Get_Returns405()
    {
        var (userId, token) = await CreateUnconfirmedUserWithTokenAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/auth/confirm-email?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}");

        response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task ConfirmEmail_PostValidToken_Returns200()
    {
        var (userId, token) = await CreateUnconfirmedUserWithTokenAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequestDto { UserId = userId, Token = token });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmEmail_PostBadToken_Returns400()
    {
        var (userId, _) = await CreateUnconfirmedUserWithTokenAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/confirm-email", new ConfirmEmailRequestDto { UserId = userId, Token = "not-a-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_ValidConfirmedUser_ReturnsTokens()
    {
        var email = $"login-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Token.ShouldNotBeNullOrEmpty();
        body.RefreshToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var email = $"badpw-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = "Wrong-Pass-9!" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_Authenticated_Returns200WithIdentity()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<CurrentUserDto>();
        body!.Email.ShouldNotBeNullOrEmpty();
        body.Roles.ShouldContain("Student");
    }

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
