using System.Globalization;
using Microsoft.AspNetCore.Http.Connections.Client;

namespace FitEdu.Web.Services;

/// <summary>
/// Sends the member's UI language as the Accept-Language header when a Blazor circuit connects to a
/// SignalR hub, like <see cref="AcceptLanguageHandler"/> does for the API. The hub connection carries
/// no culture cookie, so without this header every hub error (ChatError, CallError) came back in English.
/// </summary>
public static class HubConnectionLanguage
{
    public static void SendCurrentUiLanguage(HttpConnectionOptions options)
    {
        var languageName = CultureInfo.CurrentUICulture.Name;
        if (string.IsNullOrEmpty(languageName))
            return;

        options.Headers["Accept-Language"] = languageName;
    }
}
