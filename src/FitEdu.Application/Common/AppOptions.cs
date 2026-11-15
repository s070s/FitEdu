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

    /// <summary>
    /// True when <see cref="BaseUrl"/> can start a link: an absolute http or https address with no query
    /// or fragment (a "?x=1" or "#top" would end up in the middle of every emailed link).
    /// </summary>
    public bool HasValidBaseUrl =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var baseUri)
        && baseUri.Scheme is "http" or "https"
        && baseUri.Query.Length == 0
        && baseUri.Fragment.Length == 0;
}
