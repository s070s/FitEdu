using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.FileProviders;

namespace FitEdu.Web.Services;

/// <summary>
/// Adds a content version to the address of a file under wwwroot ("css/app.css?v=Ab12Cd34Ef56"),
/// so a returning visitor fetches a changed stylesheet instead of the old copy in their browser
/// cache. The same idea as asp-append-version, which Razor components cannot use.
/// </summary>
public sealed class StaticAssetVersions(IFileProvider webRootFiles)
{
    // Twelve URL-safe characters of the SHA-256 hash: plenty to tell two versions apart.
    private const int VersionLength = 12;

    private readonly ConcurrentDictionary<string, CachedVersion> _versions = new(StringComparer.Ordinal);

    /// <summary>The path with "?v=…" added, or the path unchanged when there is no such file.</summary>
    public string WithVersion(string path)
    {
        var file = webRootFiles.GetFileInfo(path);
        if (!file.Exists)
            return path;

        // The hash is worked out again only when the file changed (during development, say).
        var lastModified = file.LastModified;
        if (_versions.TryGetValue(path, out var cached) && cached.LastModified == lastModified)
            return cached.VersionedPath;

        var versionedPath = $"{path}?v={HashOf(file)}";
        _versions[path] = new CachedVersion(lastModified, versionedPath);
        return versionedPath;
    }

    private static string HashOf(IFileInfo file)
    {
        using var stream = file.CreateReadStream();
        var hash = SHA256.HashData(stream);
        var urlSafeHash = Convert.ToBase64String(hash).Replace('+', '-').Replace('/', '_');
        return urlSafeHash[..VersionLength];
    }

    private sealed record CachedVersion(DateTimeOffset LastModified, string VersionedPath);
}
