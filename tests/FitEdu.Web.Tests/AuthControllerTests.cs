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
        // The reason travels as a readable name, so an API client does not compare the translated message.
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"failure\":\"InvalidCredentials\"", Case.Sensitive);
        (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!.Failure.ShouldBe(AuthFailure.InvalidCredentials);
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

    // ---- Login: an unconfirmed account (the real SignInManager answers NotAllowed for it) ----

    [Theory]
    [InlineData(CustomWebAppFactory.TestPassword)]
    [InlineData("Wrong-Pass-9!")]
    public async Task Login_UnconfirmedAccount_Returns401LikeAWrongPassword(string password)
    {
        var (userId, _) = await CreateUnconfirmedUserWithTokenAsync();
        string email;
        using (var scope = _factory.Services.CreateScope())
            email = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(userId))!.Email!;
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequestDto { Email = email, Password = password });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        body!.Message.ShouldBe(ErrorMessagesRes.InvalidCredentials);
    }

    // ---- Forgot password: the same generic answer for every email ----

    [Fact]
    public async Task ForgotPassword_KnownAndUnknownEmail_Return200WithTheSameMessage()
    {
        var email = $"forgot-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var client = _factory.CreateClient();

        var known = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequestDto { Email = email });
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot-password", new ForgotPasswordRequestDto { Email = $"nobody-{Guid.NewGuid():N}@test.com" });

        known.StatusCode.ShouldBe(HttpStatusCode.OK);
        unknown.StatusCode.ShouldBe(HttpStatusCode.OK);
        var knownBody = await known.Content.ReadFromJsonAsync<AuthResponseDto>();
        var unknownBody = await unknown.Content.ReadFromJsonAsync<AuthResponseDto>();
        knownBody!.Success.ShouldBeTrue();
        knownBody.Message.ShouldBe(unknownBody!.Message);
    }

    // ---- Refresh: rotation through the real endpoint ----

    private async Task<AuthResponseDto> LoginForTokensAsync()
    {
        var email = $"refresh-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = CustomWebAppFactory.TestPassword });
        return (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
    }

    [Fact]
    public async Task Refresh_ValidToken_ReturnsANewPairAndSpendsTheOldToken()
    {
        var login = await LoginForTokensAsync();
        var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto { RefreshToken = login.RefreshToken! });
        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto { RefreshToken = login.RefreshToken! });

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var rotated = await first.Content.ReadFromJsonAsync<AuthResponseDto>();
        rotated!.Token.ShouldNotBeNullOrEmpty();
        rotated.RefreshToken.ShouldNotBe(login.RefreshToken);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshTokenRequestDto { RefreshToken = "not-a-token" });

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---- Development helpers: only the password reset is left (the e2e suite signs in through it) ----

    [Fact]
    public async Task DevConfirmEmail_IsGoneEvenInDevelopment()
    {
        var (userId, _) = await CreateUnconfirmedUserWithTokenAsync();
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var member = await userManager.FindByIdAsync(userId);

        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/dev/confirm-email", member!.Email);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await userManager.IsEmailConfirmedAsync(member)).ShouldBeFalse();
    }

#if DEBUG
    // The route exists only in Debug builds (the e2e suite runs against one); a Release build answers 404 like any unknown route.
    [Fact]
    public async Task DevResetPassword_InDevelopment_SetsThePasswordForTheE2eFixture()
    {
        var email = $"fixture-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        var client = _factory.CreateClient();

        var reset = await client.PostAsJsonAsync("/api/auth/dev/reset-password",
            new DevResetPasswordRequestDto { Email = email, NewPassword = "Fixture-Pass-9!" });
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequestDto { Email = email, Password = "Fixture-Pass-9!" });

        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
#endif
}

/// <summary>SEC-6: the development-only endpoints are not reachable on a Production host (they exist only in Debug builds, and answer 404 outside Development even there).</summary>
public class AuthControllerProductionTests : IClassFixture<ProductionWebAppFactory>
{
    private readonly ProductionWebAppFactory _factory;

    public AuthControllerProductionTests(ProductionWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DevResetPassword_OnAProductionHost_Returns404()
    {
        var client = _factory.CreateClient();
        var email = $"dev-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);

        var response = await client.PostAsJsonAsync("/api/auth/dev/reset-password",
            new DevResetPasswordRequestDto { Email = email, NewPassword = "Another-Pass-1!" });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
