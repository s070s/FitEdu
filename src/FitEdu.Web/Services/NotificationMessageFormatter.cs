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
    public static string Format(NotificationDto notification)
    {
        var template = NotificationRes.ResourceManager.GetString(notification.TitleKey, CultureInfo.CurrentUICulture);
        if (string.IsNullOrEmpty(template))
            return notification.TitleKey; // Unknown key (e.g. app rolled back after a newer key shipped) — degrade visibly, not blank.

        try
        {
            return string.Format(template, notification.BodyArgs.Cast<object>().ToArray());
        }
        catch (FormatException)
        {
            return template;
        }
    }
}
