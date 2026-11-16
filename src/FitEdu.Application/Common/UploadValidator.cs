using Microsoft.AspNetCore.Http;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.Common;

/// <summary>
/// The result of <see cref="UploadValidator.CheckAsync"/>: either an <see cref="Error"/> message in the caller's
/// language, or the validated, normalised <see cref="ContentType"/> to save the file under.
/// </summary>
public readonly record struct UploadCheck(string? Error, string ContentType)
{
    public bool IsValid => Error is null;
}

/// <summary>
/// The one check every upload endpoint runs (P05-13): the file is there, within the size limit, its declared content
/// type is in the endpoint's set, and its first bytes are the signature of that type. The client-declared type alone
/// is not trusted, so an SVG or HTML file sent as image/png is refused.
/// </summary>
public static class UploadValidator
{
    private const int BytesPerMegabyte = 1024 * 1024;

    /// <summary>The longest signature check looks at offset 8 + 4 bytes (WebP), so 12 bytes are read.</summary>
    private const int HeaderLength = 12;

    /// <summary>
    /// Checks <paramref name="file"/>. The first bytes are read from a stream of their own (opened and disposed here),
    /// so the caller opens the file again to save it and still reads it from the start.
    /// </summary>
    /// <param name="file">The uploaded file, or null when the request had none.</param>
    /// <param name="allowedContentTypes">The content types this endpoint accepts (from <see cref="UploadLimits"/>).</param>
    /// <param name="maxBytes">The size limit of this endpoint (from <see cref="UploadLimits"/>).</param>
    /// <param name="wrongTypeMessage">The message for a content type outside the set (names the allowed kinds).</param>
    /// <param name="cancellationToken">Cancels the read of the first bytes.</param>
    public static async Task<UploadCheck> CheckAsync(
        IFormFile? file,
        IReadOnlySet<string> allowedContentTypes,
        long maxBytes,
        string wrongTypeMessage,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return Refused(ErrorMessagesRes.NoFileProvided);

        if (file.Length > maxBytes)
            return Refused(ResourceFormat.Fill(ErrorMessagesRes.FileTooLargeFormat, maxBytes / BytesPerMegabyte));

        var contentType = NormaliseContentType(file.ContentType);
        if (!allowedContentTypes.Contains(contentType))
            return Refused(wrongTypeMessage);

        var header = new byte[HeaderLength];
        int headerLength;
        await using (var stream = file.OpenReadStream())
        {
            headerLength = await stream.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, cancellationToken);
        }

        return MatchesContentType(header.AsSpan(0, headerLength), contentType)
            ? new UploadCheck(null, contentType)
            : Refused(ErrorMessagesRes.FileContentMismatch);
    }

    /// <summary>True when the first bytes of a file are the signature of the given (normalised) content type.</summary>
    public static bool MatchesContentType(ReadOnlySpan<byte> header, string contentType) =>
        NormaliseContentType(contentType) switch
        {
            "image/jpeg" or "image/jpg" => StartsWith(header, 0, [0xFF, 0xD8, 0xFF]),
            "image/png" => StartsWith(header, 0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            "image/gif" => StartsWith(header, 0, "GIF87a"u8) || StartsWith(header, 0, "GIF89a"u8),
            "image/webp" => StartsWith(header, 0, "RIFF"u8) && StartsWith(header, 8, "WEBP"u8),
            "application/pdf" => StartsWith(header, 0, "%PDF-"u8),
            "video/mp4" => StartsWith(header, 4, "ftyp"u8),
            "video/webm" => StartsWith(header, 0, [0x1A, 0x45, 0xDF, 0xA3]),
            "video/ogg" => StartsWith(header, 0, "OggS"u8),
            _ => false
        };

    /// <summary>The media type alone, lower case: "IMAGE/PNG; charset=x" becomes "image/png".</summary>
    private static string NormaliseContentType(string? contentType) =>
        (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

    private static bool StartsWith(ReadOnlySpan<byte> header, int offset, ReadOnlySpan<byte> signature) =>
        header.Length >= offset + signature.Length && header.Slice(offset, signature.Length).SequenceEqual(signature);

    private static UploadCheck Refused(string message) => new(message, string.Empty);
}
