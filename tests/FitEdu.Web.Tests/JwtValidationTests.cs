using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// A bearer token must name a user who still exists, is enabled and has not had their security
/// stamp rotated; every other signed token is refused with 401 on an [Authorize] endpoint.
/// </summary>
public class JwtValidationTests : IClassFixture<CustomWebAppFactory>
{
    private const string AuthorizedEndpoint = "/api/auth/me";
    private const string AdminEndpoint = "/api/admin/categories";

    private readonly CustomWebAppFactory _factory;

    public JwtValidationTests(CustomWebAppFactory factory) => _factory = factory;

    private async Task<HttpStatusCode> GetStatusAsync(string token, string endpoint = AuthorizedEndpoint)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync(endpoint)).StatusCode;
    }

    /// <summary>A token signed with the test host's own key, carrying only the given claims.</summary>
    private static string SignedToken(IEnumerable<Claim> claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("Jwt__Key")!));
        var token = new JwtSecurityToken(
            issuer: "FitEdu.Tests",
            audience: "FitEdu.Tests",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<(ApplicationUser User, string Token)> CreateMemberWithTokenAsync()
    {
        var email = $"jwt-{Guid.NewGuid():N}@test.com";
        await _factory.CreateConfirmedUserAsync(email, CustomWebAppFactory.TestPassword);
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await userManager.FindByEmailAsync(email))!;
        var (token, _) = await scope.ServiceProvider.GetRequiredService<ITokenService>().GenerateAccessTokenAsync(user);
        return (user, token);
    }

    [Fact]
    public async Task Token_ForAnEnabledMember_IsAccepted()
    {
        var (_, token) = await CreateMemberWithTokenAsync();

        (await GetStatusAsync(token)).ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_WithoutAUserId_Returns401()
    {
        // The Admin role claim alone would pass an admin endpoint's role check, so that is where the gap shows.
        var token = SignedToken([new Claim(ClaimTypes.Email, "nobody@test.com"), new Claim(ClaimTypes.Role, "Admin")]);

        (await GetStatusAsync(token, AdminEndpoint)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_ForAUserWhoNoLongerExists_Returns401()
    {
        var token = SignedToken([new Claim(ClaimTypes.NameIdentifier, "no-such-user"), new Claim("securityStamp", "x")]);

        (await GetStatusAsync(token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_ForADisabledUser_Returns401()
    {
        var (user, token) = await CreateMemberWithTokenAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = (await userManager.FindByIdAsync(user.Id))!;
            stored.IsEnabled = false;
            await userManager.UpdateAsync(stored);
        }

        (await GetStatusAsync(token)).ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_IssuedBeforeASecurityStampChange_Returns401()
    {
        var (user, token) = await CreateMemberWithTokenAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            await userManager.UpdateSecurityStampAsync((await userManager.FindByIdAsync(user.Id))!);
        }

        (await GetStatusAsync(token)).ShouldBe(HttpStatusCode.Unauthorized);
    }
}
