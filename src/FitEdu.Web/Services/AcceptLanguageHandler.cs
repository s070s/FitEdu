using System.Globalization;
using System.Net.Http.Headers;

namespace FitEdu.Web.Services;

/// <summary>
/// DelegatingHandler for the loopback API clients. Sends the member's UI language as the
/// Accept-Language header, so the API's request localization picks the same language as the page:
/// server messages and Identity errors then come back in Greek for a Greek page. The loopback call
/// carries no culture cookie, so without this header every API answer was English.
/// </summary>
public sealed class AcceptLanguageHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var languageName = CultureInfo.CurrentUICulture.Name;
        var callerChoseALanguage = request.Headers.AcceptLanguage.Count > 0;
        if (!callerChoseALanguage && !string.IsNullOrEmpty(languageName))
        {
            request.Headers.AcceptLanguage.Add(new StringWithQualityHeaderValue(languageName));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
