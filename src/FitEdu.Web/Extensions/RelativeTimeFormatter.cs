using System.Globalization;
using FitEdu.Application.Common;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Localized "last seen X ago" strings for presence indicators. Buckets coarsen with age
/// (minutes → hours → days) and fall back to a plain date in the members' time zone beyond a week, where a
/// relative phrase stops being meaningful.
/// </summary>
public static class RelativeTimeFormatter
{
    public static string LastSeen(DateTime lastSeenUtc)
    {
        var delta = DateTime.UtcNow - lastSeenUtc;
        return delta switch
        {
            { TotalMinutes: < 1 } => GlobalRes.LastSeenJustNow,
            { TotalHours: < 1 } => ResourceFormat.Fill(GlobalRes.LastSeenMinutesFormat, (int)delta.TotalMinutes),
            { TotalDays: < 1 } => ResourceFormat.Fill(GlobalRes.LastSeenHoursFormat, (int)delta.TotalHours),
            { TotalDays: <= 7 } => ResourceFormat.Fill(GlobalRes.LastSeenDaysFormat, (int)delta.TotalDays),
            _ => ResourceFormat.Fill(GlobalRes.LastSeenDateFormat, SiteClock.ToSiteTime(lastSeenUtc).ToString("d", CultureInfo.CurrentCulture))
        };
    }

    /// <summary>
    /// Short age of a notification ("now", "5m", "3h", "2d"); older than a week shows the
    /// day and month in the members' time zone. Used by the header bell and the notifications page.
    /// </summary>
    public static string NotificationAge(DateTimeOffset createdAt)
    {
        var age = DateTimeOffset.UtcNow - createdAt;
        if (age < TimeSpan.FromMinutes(1)) return NotificationRes.TimeJustNow;
        if (age < TimeSpan.FromHours(1)) return ResourceFormat.Fill(NotificationRes.TimeMinutesFormat, (int)age.TotalMinutes);
        if (age < TimeSpan.FromDays(1)) return ResourceFormat.Fill(NotificationRes.TimeHoursFormat, (int)age.TotalHours);
        if (age < TimeSpan.FromDays(7)) return ResourceFormat.Fill(NotificationRes.TimeDaysFormat, (int)age.TotalDays);
        return SiteClock.ToSiteTime(createdAt).ToString("M", CultureInfo.CurrentCulture);
    }
}
