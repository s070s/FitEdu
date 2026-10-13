namespace FitEdu.Application.Common;

/// <summary>Settings of the "Site" configuration section.</summary>
public sealed class SiteOptions
{
    public const string SectionName = "Site";

    /// <summary>The members' time zone (an IANA id such as "Europe/Athens"): days and times are shown in it.</summary>
    public string TimeZone { get; set; } = SiteClock.DefaultTimeZoneId;
}
