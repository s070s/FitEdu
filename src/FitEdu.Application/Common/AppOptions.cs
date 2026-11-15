namespace FitEdu.Application.Common;

/// <summary>Settings of the "App" configuration section.</summary>
public sealed class AppOptions
{
    public const string SectionName = "App";

    /// <summary>
    /// The public address of the site, for example "https://fitedu.com". The links in emails start with
    /// it, so a request's Host header can never decide where a reset link points.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;
}
