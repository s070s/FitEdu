using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>Localized labels for a live class's <see cref="ScheduledSessionStatus"/>.</summary>
public static class SessionLabels
{
    public static string Status(ScheduledSessionStatus status) => status switch
    {
        ScheduledSessionStatus.Scheduled => SessionRes.StatusScheduled,
        ScheduledSessionStatus.Live => SessionRes.StatusLive,
        ScheduledSessionStatus.Ended => SessionRes.StatusEnded,
        ScheduledSessionStatus.Cancelled => SessionRes.StatusCancelled,
        _ => status.ToString()
    };

    /// <summary>Label for a status sent as its enum name (the session DTOs); unknown names are shown as they are.</summary>
    public static string Status(string statusName) =>
        Enum.TryParse<ScheduledSessionStatus>(statusName, out var status) ? Status(status) : statusName;
}
