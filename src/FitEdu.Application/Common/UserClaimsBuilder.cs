using System.Globalization;
using System.Security.Claims;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.Common;

/// <summary>
/// Single source of truth for the claim set derived from a signed-in <see cref="ApplicationUser"/>.
/// Used both by <c>TokenService</c> (JWT access tokens) and the <c>/auth/complete</c> minimal
/// endpoint (cookie principal for the Blazor circuit) so a new claim is added once instead of
/// drifting across mint sites. <see cref="ClaimTypes"/> values are used throughout — they read
/// back correctly whether attached directly to a cookie principal or round-tripped through a JWT
/// (the default <c>JwtSecurityTokenHandler</c> inbound map only rewrites short registered names
/// like "sub"/"email", leaving already-long claim types like these untouched).
/// </summary>
public static class UserClaimsBuilder
{
    /// <summary>Claim carrying the admin's id while that admin impersonates this user.</summary>
    public const string ImpersonatedByClaim = "impersonatedBy";

    public static List<Claim> Build(
        ApplicationUser user,
        IEnumerable<string> roles,
        SubscriptionTier tier,
        string? adminBackupId = null)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email!),
            new("firstName", user.FirstName),
            new("lastName", user.LastName),
            new("status", ((int)user.Status).ToString(CultureInfo.InvariantCulture)),
            new("isEnabled", user.IsEnabled.ToString().ToLowerInvariant()),
            new("subscriptionTier", ((int)tier).ToString(CultureInfo.InvariantCulture)),
            new("securityStamp", user.SecurityStamp ?? string.Empty)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        if (!string.IsNullOrEmpty(adminBackupId))
            claims.Add(new Claim(ImpersonatedByClaim, adminBackupId));

        return claims;
    }
}
