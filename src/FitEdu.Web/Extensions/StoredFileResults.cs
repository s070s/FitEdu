using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Serves a stored file the way every file endpoint should (P05-18): with <c>Last-Modified</c>, an <c>ETag</c> (so a
/// repeat request gets 304 Not Modified), range support (a video can seek, an interrupted download can resume) and
/// the endpoint's own <c>Cache-Control</c>.
/// </summary>
public static class StoredFileResults
{
    /// <summary>
    /// The file result for <paramref name="path"/>, or null when there is no such file (also when it is deleted between
    /// the check and the open, which is a 404 and not a 500, P05-19). The stream is opened only here, when the file is
    /// really returned; the result disposes it.
    /// </summary>
    /// <param name="controller">The controller answering the request.</param>
    /// <param name="storage">The file storage.</param>
    /// <param name="path">The stored relative path.</param>
    /// <param name="cacheControl">The <c>Cache-Control</c> value for this endpoint.</param>
    /// <param name="cancellationToken">The request's cancellation token.</param>
    public static async Task<IActionResult?> TryServeStoredFileAsync(
        this ControllerBase controller, IFileStorage storage, string path, string cacheControl, CancellationToken cancellationToken)
    {
        var info = storage.GetFileInfo(path);
        if (info is null)
            return null;

        Stream stream;
        string contentType;
        try
        {
            (stream, contentType) = await storage.GetFileAsync(path, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        controller.Response.Headers.CacheControl = cacheControl;
        // Unchanged size and time mean unchanged content; the storage never rewrites a file in place.
        var entityTag = new EntityTagHeaderValue($"\"{info.Value.LastModified.UtcTicks:x}-{info.Value.Length:x}\"");
        return controller.File(stream, contentType, info.Value.LastModified, entityTag, enableRangeProcessing: true);
    }
}
