namespace FitEdu.Web.Extensions;

/// <summary>
/// Turns a stored media path into a URL the browser can load. Uploads are stored as folder-relative
/// paths ("blog/covers/photo.jpg") and served by GET /api/media/…; files shipped in wwwroot
/// ("/images/blog/cover.webp") and full http(s) URLs pass through unchanged.
/// </summary>
public static class MediaUrls
{
    public const string PublicMediaPrefix = "/api/media/";

    /// <summary>The browser URL for a stored path, or null when there is no path.</summary>
    public static string? Resolve(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        var path = storedPath.Trim();
        if (IsSiteRootOrFullUrl(path))
            return path;

        return PublicMediaPrefix + path;
    }

    private static bool IsSiteRootOrFullUrl(string path) =>
        path.StartsWith('/')
        || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
