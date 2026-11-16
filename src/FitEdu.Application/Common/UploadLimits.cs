namespace FitEdu.Application.Common;

/// <summary>
/// One source for upload size limits and accepted content types, shared by the Blazor consumers, the API
/// controllers and the file storage, so the client and the server can no longer disagree.
/// </summary>
public static class UploadLimits
{
    /// <summary>Profile photos and testimonial photos.</summary>
    public const long MaxImageBytes = 5L * 1024 * 1024;

    /// <summary>Blog covers and the site background image.</summary>
    public const long MaxCoverImageBytes = 8L * 1024 * 1024;

    /// <summary>Lesson PDFs.</summary>
    public const long MaxPdfBytes = 20L * 1024 * 1024;

    /// <summary>Lesson videos.</summary>
    public const long MaxVideoBytes = 500L * 1024 * 1024;

    public static readonly IReadOnlySet<string> ImageContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/jpg", "image/png", "image/gif", "image/webp" };

    public static readonly IReadOnlySet<string> PdfContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "application/pdf" };

    public static readonly IReadOnlySet<string> VideoContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "video/mp4", "video/webm", "video/ogg" };
}
