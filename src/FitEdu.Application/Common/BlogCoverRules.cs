namespace FitEdu.Application.Common;

/// <summary>The one rule for a blog cover path, shared by the request check and the editor page.</summary>
public static class BlogCoverRules
{
    // A cover must be a file of this site: no protocol-relative start (with spaces or backslashes before it) and no "://".
    public const string SiteFilePattern = @"^(?!\s*[/\\][/\\])(?![\s\S]*://)[\s\S]*$";
}
