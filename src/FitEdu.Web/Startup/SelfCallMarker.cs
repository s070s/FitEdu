using System.Security.Cryptography;
using System.Text;

namespace FitEdu.Web.Startup;

/// <summary>
/// Tells the app's own API calls apart from an outside caller's. Outside Development the app calls
/// itself at its real public address (<c>SelfBaseUrl</c>), so those calls reach it from an outside
/// address, past a proxy: without a mark, every visitor's page load would count against one shared
/// rate-limit bucket. The own HttpClients send the secret in <see cref="HeaderName"/> to the app's
/// own address only (<see cref="FitEdu.Web.Services.SelfCallHandler"/>); the rate limiters skip a request whose header
/// matches. The secret is configured with <c>SelfCall:Secret</c> (needed when several instances
/// call each other through a load balancer) or else a random one is made at every start. It is never
/// logged.
/// </summary>
public sealed class SelfCallMarker
{
    public const string HeaderName = "X-FitEdu-Self-Call";

    public const string SecretSetting = "SelfCall:Secret";

    /// <summary>The shortest configured secret that cannot be guessed.</summary>
    public const int MinimumSecretLength = 32;

    private const int GeneratedSecretBytes = 32;

    private const char PrintableAsciiStart = '!';

    private const char PrintableAsciiEnd = '~';

    private readonly string _secret;
    private readonly byte[] _secretBytes;
    private readonly Uri _selfBaseAddress;

    public SelfCallMarker(string? configuredSecret, Uri selfBaseAddress)
    {
        _selfBaseAddress = selfBaseAddress;
        _secret = string.IsNullOrWhiteSpace(configuredSecret)
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(GeneratedSecretBytes))
            : configuredSecret;
        // A header value cannot carry spaces at its ends or non-printable characters unchanged, so such a
        // secret would never match; the message does not name the value.
        var hasUnsafeCharacter = _secret.Trim() != _secret || _secret.Any(character => character < PrintableAsciiStart || character > PrintableAsciiEnd);
        if (_secret.Length < MinimumSecretLength || hasUnsafeCharacter)
        {
            throw new InvalidOperationException(
                $"{SecretSetting} must be at least {MinimumSecretLength} printable characters without spaces at its ends, or left unset to use a random secret.");
        }

        _secretBytes = Encoding.UTF8.GetBytes(_secret);
    }

    /// <summary>
    /// Marks a request that goes to the app's own address, and removes the header from any other, so
    /// the secret never reaches another host.
    /// </summary>
    public void Stamp(HttpRequestMessage request)
    {
        request.Headers.Remove(HeaderName);
        if (!IsSelfAddress(request.RequestUri))
            return;

        request.Headers.TryAddWithoutValidation(HeaderName, _secret);
    }

    /// <summary>Whether the address has the scheme, host and port of the app's own base address.</summary>
    public bool IsSelfAddress(Uri? address)
    {
        if (address is null || !address.IsAbsoluteUri)
            return false;

        return Uri.Compare(address, _selfBaseAddress, UriComponents.SchemeAndServer, UriFormat.UriEscaped,
            StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>Whether the request carries exactly this app's secret.</summary>
    public bool IsFromSelf(HttpContext httpContext)
    {
        var presented = httpContext.Request.Headers[HeaderName];
        if (presented.Count != 1 || presented[0] is not { } presentedSecret)
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presentedSecret), _secretBytes);
    }

    /// <summary>Whether the request is one of the app's own calls (false when the app has no marker, as in a unit test).</summary>
    public static bool IsSelfCall(HttpContext httpContext)
    {
        var marker = httpContext.RequestServices?.GetService<SelfCallMarker>();
        return marker is not null && marker.IsFromSelf(httpContext);
    }
}
