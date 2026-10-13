using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Interfaces;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the live-classes schedule: classes are grouped under one heading per day in the
/// members' time zone, Europe/Athens (today and tomorrow are named), a live class offers the join button and an empty list shows
/// the empty state.
/// </summary>
public class SessionsPageTests : BunitContext
{
    private const int MiddayHour = 12;
    private const int DaysAhead = 5;

    private readonly ISessionConsumer _sessionConsumer = Substitute.For<ISessionConsumer>();

    public SessionsPageTests()
    {
        Services.AddSingleton(_sessionConsumer);
        Services.AddSingleton(Substitute.For<ICallService>());
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        AddAuthorization();
    }

    [Fact]
    public void Sessions_GroupedByDay_RendersOneHeaderPerDay()
    {
        var today = LocalMidday(0);
        ReturnSessions(
            Session("Morning mobility", today),
            Session("Lunch HIIT", today.AddHours(1)),
            Session("Strength basics", LocalMidday(1)),
            Session("Core circuit", LocalMidday(DaysAhead)));

        var page = Render<Sessions>();

        page.FindAll(".schedule-day").Count.ShouldBe(3);
        var headings = page.FindAll("h2.schedule-date");
        headings[0].TextContent.ShouldContain(SessionRes.Today);
        headings[1].TextContent.ShouldContain(SessionRes.Tomorrow);
        headings[2].QuerySelector(".schedule-date__relative").ShouldBeNull();
        page.FindAll(".schedule-day")[0].QuerySelectorAll(".session-card").Length.ShouldBe(2);
    }

    [Fact]
    public void LiveSession_ShowsJoinButton()
    {
        ReturnSessions(Session("Live yoga", LocalMidday(0)) with { Status = "Live", IsRegistered = true });

        var page = Render<Sessions>();

        var joinButton = page.Find(".session-actions .btn-success");
        joinButton.TextContent.ShouldContain(SessionRes.JoinSession);
        page.Find(".status-chip.status-live").ShouldNotBeNull();
    }

    [Fact]
    public void RegisteredSession_ShowsBadgeAndUnregister()
    {
        ReturnSessions(Session("Mobility flow", LocalMidday(1)) with { IsRegistered = true });

        var page = Render<Sessions>();

        page.Find(".registered-badge").TextContent.ShouldContain(SessionRes.Registered);
        page.Find(".session-actions .btn-cta--ghost").TextContent.ShouldContain(SessionRes.Unregister);
    }

    [Fact]
    public void StartTime_IsShownInTheMembersTimeZone()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-GB");
        CultureInfo.CurrentUICulture = new CultureInfo("en-GB");
        // 18:00 UTC on 20 October is 21:00 in Athens (UTC+3), whatever the zone of the test machine.
        ReturnSessions(Session("Evening stretch", new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero)));

        var page = Render<Sessions>();

        page.Find(".session-time__start").TextContent.ShouldBe("21:00");
    }

    [Fact]
    public void NoSessions_ShowsEmptyState()
    {
        ReturnSessions();

        var page = Render<Sessions>();

        page.Find(".empty-state__title").TextContent.ShouldBe(SessionRes.NoUpcomingSessions);
        page.FindAll(".schedule-day").ShouldBeEmpty();
    }

    private void ReturnSessions(params ScheduledSessionListItemDto[] sessions) =>
        _sessionConsumer.GetUpcomingAsync(Arg.Any<string>()).Returns(sessions);

    // Midday in the members' time zone keeps every class on the intended day of that zone.
    private static DateTimeOffset LocalMidday(int daysFromToday)
    {
        var siteToday = SiteClock.ToSiteTime(DateTimeOffset.UtcNow).Date;
        return SiteClock.FromSiteTime(siteToday.AddDays(daysFromToday).AddHours(MiddayHour));
    }

    private static ScheduledSessionListItemDto Session(string title, DateTimeOffset startsAt) => new(
        Guid.NewGuid(), title, "Coach Maria", null, startsAt.ToUniversalTime(), 45, 6, 2, "Scheduled",
        IsHost: false, IsRegistered: false, CallSessionId: null, OtherParticipantUserIds: ["other-user"]);
}
