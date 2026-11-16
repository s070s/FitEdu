using Microsoft.Extensions.Configuration;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Local file system implementation of IFileStorage.
/// Stores files under the configured uploads directory (default ./App_Data/Uploads/{folder}/{guid}{extension}).
/// </summary>
public partial class LocalFileStorage : IFileStorage
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _basePath;
    private readonly string _basePathWithSeparator;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IWebHostEnvironment environment, IConfiguration configuration, ILogger<LocalFileStorage> logger)
    {
        // CLOUD-1: allow the uploads directory to live outside the deploy folder (so a "delete and
        // re-copy" update isn't destructive) by setting Storage:UploadsPath; default stays inside
        // the content root for zero-config local development.
        var configuredPath = configuration["Storage:UploadsPath"];
        _basePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(environment.ContentRootPath, "App_Data", "Uploads")
            : configuredPath;
        _logger = logger;

        // Ensure base directory exists
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }

        _basePathWithSeparator = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_basePath)) + Path.DirectorySeparatorChar;
    }

    public async Task<string> SaveFileAsync(Stream fileStream, string contentType, string folder, long? maxBytes = null, CancellationToken cancellationToken = default)
    {
        // The extension comes from the validated content type; the client's own file name is never used.
        var extension = ExtensionFor(contentType);

        // Validate folder
        var folderPath = ResolveInsideRoot(folder);
        folder = folder.Replace('\\', '/').TrimEnd('/');

        // A fresh GUID is the whole name, so nothing the client typed reaches the path or the log.
        var uniqueFileName = $"{Guid.NewGuid():N}{extension}";

        // Create folder structure
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        // Build full path
        var filePath = Path.Combine(folderPath, uniqueFileName);

        // Validate file size — prefer explicit cap; fall back to folder-heuristic.
        ValidateFileSize(fileStream, folder, maxBytes);

        // Save file. CreateNew never overwrites another file; a copy that fails or is cancelled must not leave
        // its half-written file behind (the caller only learns the path when this method returns).
        var fileStreamOut = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        try
        {
            await fileStream.CopyToAsync(fileStreamOut, cancellationToken);
            await fileStreamOut.DisposeAsync();
        }
        catch
        {
            await fileStreamOut.DisposeAsync();
            DeletePartialFile(filePath);
            throw;
        }

        // Return relative path
        var relativePath = $"{folder}/{uniqueFileName}";
        LogFileSaved(_logger, relativePath);

        return relativePath;
    }

    public Task<(Stream stream, string contentType)> GetFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveInsideRoot(filePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File not found", filePath);
        }

        // Open file stream
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

        // Determine content type from extension
        var contentType = GetContentType(Path.GetExtension(fullPath));

        return Task.FromResult((stream as Stream, contentType));
    }

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolveInsideRoot(filePath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            LogFileDeleted(_logger, filePath);
        }

        return Task.CompletedTask;
    }

    public bool FileExists(string filePath)
    {
        try
        {
            return File.Exists(ResolveInsideRoot(filePath));
        }
        catch (ArgumentException)
        {
            // A path outside the uploads folder names no stored file.
            return false;
        }
    }

    /// <summary>
    /// The one place a stored relative path becomes a full path. Rejects an absolute path, a drive or stream
    /// colon, control characters and ".." (Path.Combine drops the uploads folder when its second part is
    /// rooted), then requires the full path to start with the uploads folder and a separator.
    /// </summary>
    /// <exception cref="ArgumentException">The path is empty or does not stay inside the uploads folder.</exception>
    private string ResolveInsideRoot(string relativePath)
    {
        var normalized = (relativePath ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        var isUnsafe = string.IsNullOrWhiteSpace(normalized)
            || normalized.Contains("..", StringComparison.Ordinal)
            || normalized.Contains(':')
            || normalized.Any(char.IsControl)
            || Path.IsPathRooted(normalized);
        if (isUnsafe)
        {
            throw new ArgumentException("Invalid file path", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, normalized));
        if (!fullPath.StartsWith(_basePathWithSeparator, PathComparison))
        {
            throw new ArgumentException("Invalid file path", nameof(relativePath));
        }

        return fullPath;
    }

    private void DeletePartialFile(string fullPath)
    {
        try
        {
            File.Delete(fullPath);
        }
        catch (IOException exception)
        {
            LogPartialFileNotDeleted(_logger, exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            LogPartialFileNotDeleted(_logger, exception);
        }
    }

    private static void ValidateFileSize(Stream fileStream, string folder, long? explicitMaxBytes)
    {
        // Network streams (e.g. from IFormFile) may not be seekable;
        // skip stream-level validation — callers should pre-check IFormFile.Length.
        if (!fileStream.CanSeek)
            return;

        var fileSize = fileStream.Length;

        // Prefer the explicit cap passed by the caller. The folder-name heuristic is the
        // last resort — new folders that don't match any keyword will throw rather than
        // silently inheriting an arbitrary default.
        long maxSize = explicitMaxBytes ?? folder.ToLowerInvariant() switch
        {
            var f when f.Contains("avatar") => UploadLimits.MaxImageBytes,
            var f when f.Contains("pdf") => UploadLimits.MaxPdfBytes,
            var f when f.Contains("video") => UploadLimits.MaxVideoBytes,
            var f when f.Contains("background") => UploadLimits.MaxCoverImageBytes,
            var f when f.Contains("cover") => UploadLimits.MaxCoverImageBytes,
            var f when f.Contains("blog") => UploadLimits.MaxCoverImageBytes,
            var f when f.Contains("certificate") => UploadLimits.MaxPdfBytes,
            _ => throw new InvalidOperationException(
                $"No file-size limit configured for folder '{folder}'. " +
                "Pass an explicit maxBytes to SaveFileAsync or add a folder mapping.")
        };

        if (fileSize > maxSize)
        {
            throw new InvalidOperationException($"File size ({fileSize} bytes) exceeds maximum allowed size ({maxSize} bytes)");
        }
    }

    /// <summary>The stored extension for a content type in the upload sets; anything else is refused.</summary>
    private static string ExtensionFor(string contentType)
    {
        var mediaType = (contentType ?? string.Empty).Split(';')[0].Trim();
        var isKnown = UploadLimits.ImageContentTypes.Contains(mediaType)
            || UploadLimits.PdfContentTypes.Contains(mediaType)
            || UploadLimits.VideoContentTypes.Contains(mediaType);
        if (!isKnown)
        {
            throw new ArgumentException("Content type is not an accepted upload type", nameof(contentType));
        }

        return mediaType.ToLowerInvariant() switch
        {
            "image/jpeg" or "image/jpg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            _ => ".ogv" // video/ogg
        };
    }

    // Also reads the extensions of files stored under the old client-name scheme (.ogg, .jpeg).
    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" or ".ogv" => "video/ogg",
            _ => "application/octet-stream"
        };
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "File saved: {FilePath}")]
    private static partial void LogFileSaved(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Information, Message = "File deleted: {FilePath}")]
    private static partial void LogFileDeleted(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A half-written upload could not be deleted.")]
    private static partial void LogPartialFileNotDeleted(ILogger logger, Exception exception);
}
