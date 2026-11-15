using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Generates JWT access tokens and opaque refresh tokens.
/// Access tokens are short-lived (configurable). Refresh tokens are opaque strings stored server-side.
/// </summary>
public class TokenService : ITokenService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtTokenSigner _signer;

    public TokenService(JwtTokenSigner signer, UserManager<ApplicationUser> userManager)
    {
        _signer = signer;
        _userManager = userManager;
    }

    public Task<(string AccessToken, DateTime Expiration)> GenerateAccessTokenAsync(ApplicationUser user) =>
        MintAsync(user, adminId: null);

    public string SignClaims(IEnumerable<Claim> claims) => _signer.Sign(claims, _signer.NewExpiration());

    /// <summary>
    /// Generates a cryptographically secure opaque refresh token.
    /// Store this server-side associated with the user.
    /// </summary>
    public string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }

    public Task<(string AccessToken, DateTime Expiration)> GenerateImpersonationTokenAsync(ApplicationUser user, string adminId) =>
        MintAsync(user, adminId);

    // The one place a user's access token is built: login and impersonation differ only in the
    // impersonatedBy claim (adminId), so they cannot drift apart.
    private async Task<(string AccessToken, DateTime Expiration)> MintAsync(ApplicationUser user, string? adminId)
    {
        var roles = await _userManager.GetRolesAsync(user);

        var claims = UserClaimsBuilder.Build(user, roles, adminBackupId: adminId);
        claims.Add(new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));

        var expiration = _signer.NewExpiration();
        return (_signer.Sign(claims, expiration), expiration);
    }
}
