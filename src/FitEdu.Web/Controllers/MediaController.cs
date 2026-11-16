using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Public endpoint for serving non-sensitive uploaded media files (blog covers, testimonial avatars).
/// Restricts access to an explicit allowlist of public folders to prevent directory traversal or
/// exposure of sensitive uploads (e.g. lesson PDFs, avatars).
/// </summary>
[ApiController]
[Route("api/media")]
[Tags("Media")]
[ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "image/jpeg", "image/png", "image/gif", "image/webp")]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class MediaController : ControllerBase
{
    private static readonly HashSet<string> PublicFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "blog/covers",
        "testimonials/avatars"
    };

    // Only these extensions may be served through the public media endpoint: the image types an upload can have
    // (UploadLimits.ImageContentTypes). SVG and AVIF can never be uploaded, so they are not served either; the list
    // also prevents content-type attacks with a crafted extension (e.g. .html) that would run script in the browser.
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp"
    };

    /// <summary>
    /// Uploaded files have a name no other file ever gets (a GUID, or on older files the client's name plus a GUID) and are
    /// never rewritten, so a browser may keep them for a year (P05-18).
    /// </summary>
    private const string CacheControlValue = "public, max-age=31536000, immutable";

    private readonly IFileStorage _fileStorage;

    public MediaController(IFileStorage fileStorage)
    {
        _fileStorage = fileStorage;
    }

    /// <summary>
    /// Serves an uploaded file if it lives within an allowed public folder.
    /// Example: GET /api/media/testimonials/avatars/photo_abc123.jpg
    /// </summary>
    [HttpGet("{*filePath}")]
    public async Task<IActionResult> GetFile(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || filePath.Contains(".."))
            return Problem(detail: ErrorMessagesRes.InvalidFilePath, statusCode: StatusCodes.Status400BadRequest);

        // Normalise to forward slashes
        filePath = filePath.Replace('\\', '/').Trim('/');

        // Only serve files from explicitly allowed public folders
        var allowed = PublicFolders.Any(folder =>
            filePath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        if (!allowed)
            return NotFound();

        // Reject any extension not on the explicit allowlist.
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            return NotFound();

        // No Content-Disposition: an image shown in the page needs none, and a stored name with non-ASCII letters or a
        // quote in it made the header invalid (a 500 for Greek file names, P05-2).
        var result = await this.TryServeStoredFileAsync(_fileStorage, filePath, CacheControlValue, cancellationToken);
        if (result is null)
            return NotFound();

        // Prevent MIME-sniffing attacks
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        // Neutralise script-carrying uploads (e.g. SVG). The `sandbox` directive runs the
        // response as a sandboxed document with scripts disabled when navigated directly,
        // while still allowing display via <img>. `default-src 'none'` blocks any outbound
        // fetch the file might attempt.
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

        return result;
    }
}
