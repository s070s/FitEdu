using FitEdu.Application.Common;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// The members' clock runs in the site time zone (Europe/Athens, the default the tests keep), not
/// in UTC or the zone of the machine that runs the tests.
/// </summary>
public class SiteClockTests
{
    // Tuesday 13 October 2026, 22:30 UTC: already 01:30 on Wednesday in Athens (UTC+3 in summer time).
    private static readonly DateTimeOffset LateEveningUtc = new(2026, 10, 13, 22, 30, 0, TimeSpan.Zero);

    [Fact]
    public void TheDefaultZone_IsAthens()
    {
        SiteClock.TimeZone.HasIanaId.ShouldBeTrue();
        SiteClock.TimeZone.Id.ShouldBe(SiteClock.DefaultTimeZoneId);
    }

    [Fact]
    public void Today_IsAlreadyTheNextDayInAthens_AtLateEveningUtc()
    {
        var today = SiteClock.Today(new FixedTimeProvider(LateEveningUtc));

        today.ShouldBe(new DateOnly(2026, 10, 14));
    }

    [Fact]
    public void ToSiteTime_ShowsTheAthensWallClock_InSummerAndWinter()
    {
        var summer = SiteClock.ToSiteTime(new DateTimeOffset(2026, 7, 1, 18, 0, 0, TimeSpan.Zero));
        var winter = SiteClock.ToSiteTime(new DateTimeOffset(2026, 12, 1, 18, 0, 0, TimeSpan.Zero));

        summer.Hour.ShouldBe(21);
        summer.Offset.ShouldBe(TimeSpan.FromHours(3));
        winter.Hour.ShouldBe(20);
        winter.Offset.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void ToSiteTime_ReadsAStoredDateTimeAsUtc_WhateverItsKind()
    {
        var stored = new DateTime(2026, 10, 13, 22, 30, 0, DateTimeKind.Unspecified);

        var siteTime = SiteClock.ToSiteTime(stored);

        siteTime.ShouldBe(new DateTimeOffset(2026, 10, 14, 1, 30, 0, TimeSpan.FromHours(3)));
        SiteClock.ToSiteTime(DateTime.SpecifyKind(stored, DateTimeKind.Utc)).ShouldBe(siteTime);
    }

    [Fact]
    public void FromSiteTime_TurnsAFormTimeBackIntoTheSameUtcMoment()
    {
        var typedInTheForm = new DateTime(2026, 10, 20, 21, 0, 0);

        var utc = SiteClock.FromSiteTime(typedInTheForm);

        utc.ShouldBe(new DateTimeOffset(2026, 10, 20, 18, 0, 0, TimeSpan.Zero));
        SiteClock.ToSiteTime(utc).DateTime.ShouldBe(typedInTheForm);
    }

    [Fact]
    public void FromSiteTime_InTheSpringForwardGap_MeansTheHourAfter()
    {
        // On 29 March 2026 Athens clocks jump from 03:00 to 04:00, so 03:30 never happens.
        var utc = SiteClock.FromSiteTime(new DateTime(2026, 3, 29, 3, 30, 0));

        SiteClock.ToSiteTime(utc).DateTime.ShouldBe(new DateTime(2026, 3, 29, 4, 30, 0));
    }

    [Fact]
    public void UseTimeZone_RejectsAnUnknownZone()
    {
        Should.Throw<InvalidOperationException>(() => SiteClock.UseTimeZone("Mars/Olympus_Mons"));

        SiteClock.TimeZone.Id.ShouldBe(SiteClock.DefaultTimeZoneId);
    }
}
