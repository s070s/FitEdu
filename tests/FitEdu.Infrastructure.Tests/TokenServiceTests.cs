using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using NSubstitute;
using FitEdu.TestSupport;
using FitEdu.Infrastructure.ApiServices;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class TokenServiceTests
{
    private static JwtOptions Jwt(string? key = "test-signing-key-at-least-32-bytes-long-1234567890") => new()
    {
        Key = key ?? string.Empty,
        Issuer = "test-issuer",
        Audience = "test-audience",
        AccessTokenExpirationMinutes = 60
    };

    private static ApplicationUser User() => new()
    {
        Id = "user-1",
        Email = "user@example.com",
        UserName = "user@example.com",
        FirstName = "John",
        LastName = "Doe",
        Status = UserStatus.Beginner,
        IsEnabled = true,
        SecurityStamp = "stamp-1"
    };

    private static TokenService NewService(params string[] roles)
    {
        var um = IdentityMocks.MockUserManager();
        um.GetRolesAsync(Arg.Any<ApplicationUser>()).Returns(roles.ToList());
        return new TokenService(new JwtTokenSigner(Options.Create(Jwt())), um);
    }

    [Theory]
    [InlineData(null, "Jwt:Key is required")]
    [InlineData("too-short", "at least 32 bytes")]
    public void Signer_MissingOrShortJwtKey_Throws(string? key, string expectedMessagePart)
    {
        var exception = Should.Throw<InvalidOperationException>(() => new JwtTokenSigner(Options.Create(Jwt(key))));

        exception.Message.ShouldContain(expectedMessagePart);
    }

    [Fact]
    public async Task GenerateAccessToken_EmbedsExpectedClaims()
    {
        var svc = NewService("Student");

        var (token, _) = await svc.GenerateAccessTokenAsync(User());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.ShouldContain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "user-1");
        jwt.Claims.ShouldContain(c => c.Type == ClaimTypes.Email && c.Value == "user@example.com");
        jwt.Claims.ShouldContain(c => c.Type == "firstName" && c.Value == "John");
        jwt.Claims.ShouldContain(c => c.Type == "securityStamp" && c.Value == "stamp-1");
        jwt.Claims.ShouldContain(c => c.Value == "Student");
    }

    [Fact]
    public async Task GenerateAccessToken_CarriesNoClaimNothingReads()
    {
        // status, isEnabled and subscriptionTier were never read by anything (the API checks the
        // database on each request), so they are no longer put into every token.
        var svc = NewService("Student");

        var (token, _) = await svc.GenerateAccessTokenAsync(User());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.ShouldNotContain(c => c.Type == "status" || c.Type == "isEnabled" || c.Type == "subscriptionTier");
    }

    [Fact]
    public async Task GenerateAccessToken_ExpiresAroundConfiguredMinutes()
    {
        var svc = NewService();

        var (_, expiration) = await svc.GenerateAccessTokenAsync(User());

        expiration.ShouldBeInRange(DateTime.UtcNow.AddMinutes(58), DateTime.UtcNow.AddMinutes(62));
    }

    [Fact]
    public async Task GenerateImpersonationToken_IncludesImpersonatedByClaim()
    {
        var svc = NewService("Student");

        var (token, _) = await svc.GenerateImpersonationTokenAsync(User(), adminId: "admin-9");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.ShouldContain(c => c.Type == "impersonatedBy" && c.Value == "admin-9");
    }

    [Fact]
    public void SignClaims_SignsTheGivenClaimsWithTheSameKeyIssuerAudienceAndLifetime()
    {
        var svc = NewService();

        var token = svc.SignClaims([new Claim(ClaimTypes.NameIdentifier, "user-1"), new Claim("impersonatedBy", "admin-9")]);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Issuer.ShouldBe("test-issuer");
        jwt.Audiences.ShouldHaveSingleItem().ShouldBe("test-audience");
        jwt.Claims.ShouldContain(c => c.Type == ClaimTypes.NameIdentifier && c.Value == "user-1");
        jwt.Claims.ShouldContain(c => c.Type == "impersonatedBy" && c.Value == "admin-9");
        jwt.ValidTo.ShouldBeInRange(DateTime.UtcNow.AddMinutes(58), DateTime.UtcNow.AddMinutes(62));
    }

    [Fact]
    public async Task EveryAccessToken_IsVerifiedByTheSameKey()
    {
        var svc = NewService("Student");
        var parameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidIssuer = "test-issuer",
            ValidAudience = "test-audience",
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes("test-signing-key-at-least-32-bytes-long-1234567890"))
        };
        var handler = new JwtSecurityTokenHandler();

        var (login, _) = await svc.GenerateAccessTokenAsync(User());
        var (impersonation, _) = await svc.GenerateImpersonationTokenAsync(User(), "admin-9");
        var signed = svc.SignClaims([new Claim(ClaimTypes.NameIdentifier, "user-1")]);

        foreach (var token in new[] { login, impersonation, signed })
            handler.ValidateToken(token, parameters, out _);
    }

    [Fact]
    public void GenerateRefreshToken_ReturnsDistinct64ByteBase64()
    {
        var svc = NewService();

        var a = svc.GenerateRefreshToken();
        var b = svc.GenerateRefreshToken();

        a.ShouldNotBe(b);
        Convert.FromBase64String(a).Length.ShouldBe(64);
    }
}
