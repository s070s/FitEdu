using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// The one place a JWT access token is signed: key, issuer, audience and lifetime come from
/// <see cref="JwtOptions"/>. <see cref="TokenService"/> (login and impersonation tokens) and the
/// Blazor pages' API handler both sign through it, so they cannot drift apart. Register as a singleton.
/// </summary>
public sealed class JwtTokenSigner
{
    private static readonly JwtSecurityTokenHandler TokenHandler = new() { SetDefaultTimesOnTokenCreation = false };

    private readonly SigningCredentials _credentials;
    private readonly string? _issuer;
    private readonly string? _audience;
    private readonly double _accessTokenExpirationMinutes;

    public JwtTokenSigner(IOptions<JwtOptions> options)
    {
        var jwt = options.Value;
        if (jwt.KeyProblem is { } keyProblem)
            throw new InvalidOperationException(keyProblem);

        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256);
        _issuer = jwt.Issuer;
        _audience = jwt.Audience;
        _accessTokenExpirationMinutes = jwt.AccessTokenExpirationMinutes;
    }

    /// <summary>When a token signed now stops being valid.</summary>
    public DateTime NewExpiration() => DateTime.UtcNow.AddMinutes(_accessTokenExpirationMinutes);

    public string Sign(IEnumerable<Claim> claims, DateTime expiration)
    {
        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: expiration,
            signingCredentials: _credentials);

        return TokenHandler.WriteToken(token);
    }
}
