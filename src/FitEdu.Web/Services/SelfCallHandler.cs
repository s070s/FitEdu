using FitEdu.Web.Startup;

namespace FitEdu.Web.Services;

/// <summary>
/// DelegatingHandler that marks the app's own API calls (see <see cref="SelfCallMarker"/>) so the rate
/// limiters do not count them against an outside address. It marks only requests to the app's own
/// base address: a client of any other host never gets the secret.
/// </summary>
public sealed class SelfCallHandler(SelfCallMarker marker) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        marker.Stamp(request);
        return base.SendAsync(request, cancellationToken);
    }
}
