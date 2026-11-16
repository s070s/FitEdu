using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Application.Common;

/// <summary>Helpers shared by the places that replace or remove a stored upload.</summary>
public static partial class FileStorageExtensions
{
    /// <summary>
    /// Deletes a file no database row points at any more (P05-6): the one an upload replaced, once the new file is saved
    /// and the row updated, so a failed save or update never costs the old file; or the new file when the update failed.
    /// Best-effort: a failure here leaves an orphan file, which is far better than failing a request whose new file is
    /// already in use, so it is logged in one line and not thrown. Values that are not stored files (a site-relative
    /// path such as "/images/x.webp", or an http(s) URL) are skipped.
    /// </summary>
    /// <param name="storage">The file storage.</param>
    /// <param name="oldPath">The unused value; null or empty means there was none.</param>
    /// <param name="logger">Receives the one warning when the delete fails.</param>
    public static async Task DeleteUnusedFileAsync(this IFileStorage storage, string? oldPath, ILogger logger)
    {
        if (!IsStoredFile(oldPath))
            return;

        try
        {
            // Not tied to the request: the database already points at the new file, so cleaning up must not be cancelled.
            await storage.DeleteFileAsync(oldPath!, CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogOldFileNotDeleted(logger, exception, oldPath!);
        }
    }

    /// <summary>True for a path inside the uploads folder; false for empty values, "/site/paths" and "scheme://urls".</summary>
    public static bool IsStoredFile(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.StartsWith('/')
        && !path.Contains("://", StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The replaced file could not be deleted and stays as an orphan: {FilePath}")]
    private static partial void LogOldFileNotDeleted(ILogger logger, Exception exception, string filePath);
}
