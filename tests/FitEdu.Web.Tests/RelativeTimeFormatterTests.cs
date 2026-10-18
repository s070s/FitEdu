using System.Globalization;
using FitEdu.Application.Common;
using FitEdu.TestSupport;
using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public sealed class RelativeTimeFormatterTests : IDisposable
{
    // Pin the culture so the neutral (English) resources resolve regardless of runner locale;
    // Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new("en");

    [Fact]
    public void LastSeen_UnderOneMinute_SaysJustNow()
    {
        var result = RelativeTimeFormatter.LastSeen(DateTime.UtcNow.AddSeconds(-30));

        result.ShouldBe("last seen just now");
    }

    [Fact]
    public void LastSeen_UnderOneHour_SaysMinutesAgo()
    {
        var result = RelativeTimeFormatter.LastSeen(DateTime.UtcNow.AddMinutes(-30));

        result.ShouldBe("last seen 30 minutes ago");
    }

    [Fact]
    public void LastSeen_UnderOneDay_SaysHoursAgo()
    {
        var result = RelativeTimeFormatter.LastSeen(DateTime.UtcNow.AddHours(-5));

        result.ShouldBe("last seen 5 hours ago");
    }

    [Fact]
    public void LastSeen_UpToSevenDays_SaysDaysAgo()
    {
        var result = RelativeTimeFormatter.LastSeen(DateTime.UtcNow.AddDays(-3));

        result.ShouldBe("last seen 3 days ago");
    }

    [Fact]
    public void LastSeen_OneDay_UsesSingular()
    {
        var result = RelativeTimeFormatter.LastSeen(DateTime.UtcNow.AddDays(-1).AddMinutes(-5));

        result.ShouldBe("last seen 1 day ago");
    }

    [Fact]
    public void LastSeen_OlderThanSevenDays_FallsBackToDate()
    {
        var lastSeen = DateTime.UtcNow.AddDays(-30);

        var result = RelativeTimeFormatter.LastSeen(lastSeen);

        // The date is the members' day (SiteClock), not the day in the test machine's zone.
        result.ShouldBe($"last seen {SiteClock.ToSiteTime(lastSeen).ToString("d", CultureInfo.CurrentCulture)}");
    }

    [Fact]
    public void NotificationAge_UnderOneHour_ShowsMinutes()
    {
        var result = RelativeTimeFormatter.NotificationAge(DateTimeOffset.UtcNow.AddMinutes(-5));

        result.ShouldBe("5m");
    }

    public void Dispose() => _cultureScope.Dispose();
}
