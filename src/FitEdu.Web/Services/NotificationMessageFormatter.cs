using System.Globalization;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Services;

/// <summary>
/// Resolves a <see cref="NotificationDto"/>'s stored TitleKey + BodyArgs into display text in
/// the current request's culture. Shared by the bell dropdown and the full notifications page
/// so both render identically.
/// </summary>
public static class NotificationMessageFormatter
{
    // The most placeholders any notification template uses: {0} English and {1} Greek title.
    private const int MaxTemplateArguments = 2;

    public static string Format(NotificationDto notification)
    {
        var template = NotificationRes.ResourceManager.GetString(notification.TitleKey, CultureInfo.CurrentUICulture);
        if (string.IsNullOrEmpty(template))
            return notification.TitleKey; // Unknown key (e.g. app rolled back after a newer key shipped) — degrade visibly, not blank.

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, PadArguments(notification.BodyArgs));
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// Rows stored before the Greek title was added hold only the English title, so repeat the
    /// last argument up to the template count: a Greek {1} then falls back to the stored title.
    /// </summary>
    private static object[] PadArguments(List<string> bodyArgs)
    {
        var arguments = new List<object>(bodyArgs);
        if (arguments.Count == 0)
            return [];

        var lastArgument = arguments[^1];
        while (arguments.Count < MaxTemplateArguments)
            arguments.Add(lastArgument);

        return [.. arguments];
    }
}
