using System.Net.Http.Headers;
using System.Text;

namespace FitEdu.Web.Tests;

/// <summary>Small files with real signatures (or none) for the upload tests, and the multipart request around them.</summary>
internal static class UploadSamples
{
    public static byte[] Png() => Padded([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

    public static byte[] Pdf() => Padded(Encoding.ASCII.GetBytes("%PDF-1.4\n"));

    public static byte[] Mp4() => Padded([0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m']);

    public static byte[] Svg() =>
        Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

    /// <summary>A PNG signature followed by zeros, <paramref name="length"/> bytes in all.</summary>
    public static byte[] PngOfLength(int length)
    {
        var bytes = new byte[length];
        Png().AsSpan(0, 8).CopyTo(bytes);
        return bytes;
    }

    public static MultipartFormDataContent Multipart(byte[] bytes, string contentType, string fileName = "client-name.bin")
    {
        var file = new ByteArrayContent(bytes);
        if (contentType.Length > 0)
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static byte[] Padded(byte[] header)
    {
        var bytes = new byte[64];
        header.CopyTo(bytes, 0);
        return bytes;
    }
}
