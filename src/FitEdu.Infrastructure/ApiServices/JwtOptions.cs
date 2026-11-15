using System.Text;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Settings of the "Jwt" configuration section: the one place the signing key, issuer, audience and
/// token lifetime are read. The token signer, the bearer-token validation and the startup check all
/// use this object.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>HMAC-SHA256 wants a key of at least this many bytes.</summary>
    public const int MinimumKeyBytes = 32;

    public string Key { get; set; } = string.Empty;

    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    public double AccessTokenExpirationMinutes { get; set; } = 60;

    /// <summary>What is wrong with <see cref="Key"/> (for the operator reading the startup error), or null when it is fine.</summary>
    public string? KeyProblem
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Key))
                return "Jwt:Key is required. Set it via User Secrets (dev) or environment variable Jwt__Key (prod).";

            if (Encoding.UTF8.GetByteCount(Key) < MinimumKeyBytes)
                return "Jwt:Key must be at least 32 bytes for HMAC-SHA256 security.";

            return null;
        }
    }
}
