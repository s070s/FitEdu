namespace FitEdu.Application.Common;

/// <summary>
/// Dates and times in the members' time zone (Site:TimeZone, Europe/Athens by default) instead of
/// UTC or the server's own zone. The database keeps UTC; pages convert with
/// <see cref="ToSiteTime(DateTimeOffset)"/> just before they show a moment, and the training streak
/// and the dashboard date count days with <see cref="Today"/>, so "today" turns at the members'
/// midnight. The zone is set once at startup (<see cref="UseTimeZone"/>) and only read afterwards.
/// </summary>
public static class SiteClock
{
    public const string DefaultTimeZoneId = "Europe/Athens";

    private static TimeZoneInfo _timeZone = FindTimeZone(DefaultTimeZoneId);

    /// <summary>The members' time zone.</summary>
    public static TimeZoneInfo TimeZone => _timeZone;

    /// <summary>Sets the members' time zone; called once at startup with Site:TimeZone.</summary>
    public static void UseTimeZone(string timeZoneId) => _timeZone = FindTimeZone(timeZoneId);

    /// <summary>Today's date in the members' time zone.</summary>
    public static DateOnly Today(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        var siteNow = ToSiteTime(timeProvider.GetUtcNow());
        return DateOnly.FromDateTime(siteNow.DateTime);
    }

    /// <summary>The same moment as the members' wall-clock time.</summary>
    public static DateTimeOffset ToSiteTime(DateTimeOffset moment) => TimeZoneInfo.ConvertTime(moment, _timeZone);

    /// <summary>A stored UTC time (Kind Utc, or Unspecified as EF Core may read it) as the members' wall-clock time.</summary>
    public static DateTimeOffset ToSiteTime(DateTime utcTime)
    {
        var utc = utcTime.Kind == DateTimeKind.Local ? utcTime.ToUniversalTime() : DateTime.SpecifyKind(utcTime, DateTimeKind.Utc);
        return ToSiteTime(new DateTimeOffset(utc));
    }

    /// <summary>
    /// The UTC moment of a wall-clock time typed in the members' time zone (an admin's form): the
    /// reverse of <see cref="ToSiteTime(DateTimeOffset)"/>.
    /// </summary>
    public static DateTimeOffset FromSiteTime(DateTime siteTime)
    {
        var wallClock = DateTime.SpecifyKind(siteTime, DateTimeKind.Unspecified);
        // A time inside the spring-forward gap (03:00-04:00 in Athens) never happens on the clock; it
        // means the hour after it.
        if (_timeZone.IsInvalidTime(wallClock))
            wallClock = wallClock.AddHours(1);

        var utc = TimeZoneInfo.ConvertTimeToUtc(wallClock, _timeZone);
        return new DateTimeOffset(utc);
    }

    private static TimeZoneInfo FindTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var timeZone))
            throw new InvalidOperationException($"Site:TimeZone '{timeZoneId}' is not a known time zone id (for example 'Europe/Athens').");

        return timeZone;
    }
}
