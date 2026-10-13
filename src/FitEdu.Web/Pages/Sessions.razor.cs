using Microsoft.AspNetCore.Components;
using FitEdu.Application.DTOs;
using FitEdu.Web.Consumers;
using FitEdu.Web.Interfaces;
using FitEdu.Shared.Resources;
using System.Globalization;

namespace FitEdu.Web.Pages;

public partial class Sessions
{
    private const string LiveStatus = "Live";
    private const string ScheduledStatus = "Scheduled";
    private const string DayHeadingFormat = "dddd d MMMM";
    private const string DayHeadingIdPrefix = "schedule-day-";
    private const int FullPercent = 100;

    [Inject] private ISessionConsumer SessionConsumer { get; set; } = default!;
    [Inject] private ICallService CallService { get; set; } = default!;

    private List<ScheduledSessionListItemDto> _sessions = [];
    private bool _loading = true;
    private bool _joining;
    private string? _error;

    private static string CurrentLang =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "el" ? "el" : "en";

    /// <summary>The upcoming classes grouped by their local calendar day, earliest day and class first.</summary>
    private IEnumerable<IGrouping<DateTime, ScheduledSessionListItemDto>> ScheduleDays =>
        _sessions
            .OrderBy(session => session.StartsAtUtc)
            .GroupBy(session => session.StartsAtUtc.ToLocalTime().Date)
            .OrderBy(day => day.Key);

    /// <summary>"Today" or "Tomorrow" for the two nearest days, null for any other day.</summary>
    private static string? RelativeDayLabel(DateTime day)
    {
        var today = DateTime.Now.Date;
        if (day == today)
            return SessionRes.Today;
        if (day == today.AddDays(1))
            return SessionRes.Tomorrow;
        return null;
    }

    private static string DayHeadingId(DateTime day) =>
        DayHeadingIdPrefix + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool IsLive(ScheduledSessionListItemDto session) => session.Status == LiveStatus;

    private static string StatusLabel(string status) => status switch
    {
        LiveStatus => SessionRes.StatusLive,
        "Ended" => SessionRes.StatusEnded,
        "Cancelled" => SessionRes.StatusCancelled,
        _ => SessionRes.StatusScheduled
    };

    /// <summary>How full the class is, 0-100, for the capacity bar.</summary>
    private static int CapacityPercent(ScheduledSessionListItemDto session)
    {
        if (session.MaxParticipants <= 0)
            return FullPercent;

        var percent = FullPercent * session.RegisteredCount / session.MaxParticipants;
        return Math.Clamp(percent, 0, FullPercent);
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loading = true;
        _sessions = [.. await SessionConsumer.GetUpcomingAsync(CurrentLang)];
        _loading = false;
    }

    private async Task RegisterAsync(Guid id)
    {
        _error = null;
        if (await SessionConsumer.RegisterAsync(id))
            await LoadAsync();
        else
            _error = SessionRes.RegisterError;
    }

    private async Task UnregisterAsync(Guid id)
    {
        _error = null;
        if (await SessionConsumer.UnregisterAsync(id))
            await LoadAsync();
    }

    /// <summary>
    /// The first host/registrant to click this rings everyone else via the existing group-call
    /// flow (ICallService.StartCallAsync) — no server-materialized call, full reuse of the
    /// already-proven ring/accept/media stack. The resulting CallSessionId is then persisted so
    /// later visitors see "call in progress" instead of starting a second, separate call.
    /// </summary>
    private async Task JoinOrStartAsync(ScheduledSessionListItemDto session)
    {
        if (session.OtherParticipantUserIds.Count == 0)
            return;

        _error = null;
        _joining = true;
        try
        {
            await CallService.EnsureConnectedAsync();
            await CallService.StartCallAsync([.. session.OtherParticipantUserIds]);

            if (CallService.ActiveCallId is { } callId)
            {
                await SessionConsumer.LinkCallAsync(session.Id, callId);
                await LoadAsync();
            }
            else
            {
                _error = SessionRes.JoinError;
            }
        }
        finally
        {
            _joining = false;
        }
    }
}
