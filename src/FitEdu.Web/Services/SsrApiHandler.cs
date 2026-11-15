using System.Net.Http.Headers;
using FitEdu.Infrastructure.ApiServices;

namespace FitEdu.Web.Services;

/// <summary>
/// DelegatingHandler for server-side HttpClient consumers.
/// Attaches a short-lived JWT (built from the current user's cookie claims) as the
/// Authorization header so API controllers continue to receive a Bearer token when
/// the Blazor Server circuit calls them via HttpClient on the same server.
/// No database calls are made — claims are read directly from the authenticated principal.
/// </summary>
public class SsrApiHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly JwtTokenSigner _signer;

    public SsrApiHandler(IHttpContextAccessor httpContextAccessor, JwtTokenSigner signer)
    {
        _httpContextAccessor = httpContextAccessor;
        _signer = signer;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated == true)
        {
            var jwt = _signer.Sign(principal.Claims, _signer.NewExpiration());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
