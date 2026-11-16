using System.Text;
using Microsoft.AspNetCore.Http;
using FitEdu.Application.Common;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>
/// P05-13: one check for every upload: the content type is in the endpoint's set, the size is within its limit and
/// the first bytes are the signature of the type the client declared (an SVG sent as image/png is refused).
/// </summary>
public class UploadValidatorTests
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D];

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] Padded(params byte[][] parts)
    {
        var all = parts.SelectMany(part => part).ToList();
        while (all.Count < 32)
            all.Add(0);
        return all.ToArray();
    }

    private static FormFile FileOf(byte[] bytes, string contentType, long? declaredLength = null) =>
        new(new MemoryStream(bytes), 0, declaredLength ?? bytes.Length, "file", "client-name.bin")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };

    private static Task<UploadCheck> CheckImageAsync(IFormFile? file, long maxBytes = UploadLimits.MaxImageBytes) =>
        UploadValidator.CheckAsync(file, UploadLimits.ImageContentTypes, maxBytes, ErrorMessagesRes.OnlyImageFilesAllowed);

    public static TheoryData<string, byte[]> GoodSignatures => new()
    {
        { "image/png", Padded(PngHeader) },
        { "image/jpeg", Padded([0xFF, 0xD8, 0xFF, 0xE0]) },
        { "image/jpg", Padded([0xFF, 0xD8, 0xFF, 0xDB]) },
        { "image/gif", Padded(Ascii("GIF89a")) },
        { "image/webp", Padded(Ascii("RIFF"), [1, 2, 3, 4], Ascii("WEBP")) },
        { "application/pdf", Padded(Ascii("%PDF-1.7")) },
        { "video/mp4", Padded([0, 0, 0, 0x18], Ascii("ftypisom")) },
        { "video/webm", Padded([0x1A, 0x45, 0xDF, 0xA3]) },
        { "video/ogg", Padded(Ascii("OggS")) },
    };

    [Theory]
    [MemberData(nameof(GoodSignatures))]
    public void Signature_OfTheDeclaredType_Matches(string contentType, byte[] header)
    {
        UploadValidator.MatchesContentType(header, contentType).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(GoodSignatures))]
    public void Signature_OfAnotherType_DoesNotMatch(string contentType, byte[] header)
    {
        var otherType = contentType == "image/png" ? "application/pdf" : "image/png";

        UploadValidator.MatchesContentType(header, otherType).ShouldBeFalse();
    }

    [Fact]
    public async Task ValidImage_Passes_AndTheFileCanStillBeReadFromTheStart()
    {
        var file = FileOf(Padded(PngHeader), "image/png");

        var check = await CheckImageAsync(file);

        check.IsValid.ShouldBeTrue();
        check.ContentType.ShouldBe("image/png");
        using var again = file.OpenReadStream();
        again.ReadByte().ShouldBe(0x89);
    }

    [Fact]
    public async Task ContentTypeWithParameters_IsNormalised()
    {
        var check = await CheckImageAsync(FileOf(Padded(PngHeader), "IMAGE/PNG; charset=binary"));

        check.IsValid.ShouldBeTrue();
        check.ContentType.ShouldBe("image/png");
    }

    [Fact]
    public async Task SvgDeclaredAsPng_IsRefused()
    {
        var svg = Ascii("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

        var check = await CheckImageAsync(FileOf(svg, "image/png"));

        check.IsValid.ShouldBeFalse();
        check.Error.ShouldBe(ErrorMessagesRes.FileContentMismatch);
    }

    [Fact]
    public async Task HeaderShorterThanTheSignature_IsRefused()
    {
        var check = await CheckImageAsync(FileOf([0x89, 0x50, 0x4E], "image/png"));

        check.Error.ShouldBe(ErrorMessagesRes.FileContentMismatch);
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("image/avif")]
    [InlineData("text/html")]
    [InlineData("")]
    public async Task ContentTypeOutsideTheSet_IsRefusedWithTheEndpointsMessage(string contentType)
    {
        var check = await CheckImageAsync(FileOf(Padded(PngHeader), contentType));

        check.Error.ShouldBe(ErrorMessagesRes.OnlyImageFilesAllowed);
    }

    [Fact]
    public async Task NoFileOrAnEmptyFile_IsRefused()
    {
        (await CheckImageAsync(null)).Error.ShouldBe(ErrorMessagesRes.NoFileProvided);
        (await CheckImageAsync(FileOf([], "image/png"))).Error.ShouldBe(ErrorMessagesRes.NoFileProvided);
    }

    [Fact]
    public async Task FileOverTheLimit_IsRefusedWithTheLimitInTheMessage()
    {
        var file = FileOf(Padded(PngHeader), "image/png", declaredLength: UploadLimits.MaxImageBytes + 1);

        var check = await CheckImageAsync(file);

        check.Error.ShouldBe(ResourceFormat.Fill(ErrorMessagesRes.FileTooLargeFormat, 5));
    }

    [Fact]
    public async Task FileExactlyAtTheLimit_Passes()
    {
        var file = FileOf(Padded(PngHeader), "image/png", declaredLength: UploadLimits.MaxImageBytes);

        (await CheckImageAsync(file)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task TooLargeMessage_IsInTheMembersLanguage()
    {
        using var greek = new CultureScope("el");
        var file = FileOf(Padded(PngHeader), "image/png", declaredLength: UploadLimits.MaxCoverImageBytes + 1);

        var check = await UploadValidator.CheckAsync(file, UploadLimits.ImageContentTypes, UploadLimits.MaxCoverImageBytes,
            ErrorMessagesRes.OnlyImageFilesAllowed);

        check.Error.ShouldBe("Το αρχείο είναι πολύ μεγάλο (έως 8 MB).");
    }
}
