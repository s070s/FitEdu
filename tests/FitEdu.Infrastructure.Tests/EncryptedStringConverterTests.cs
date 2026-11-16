using Microsoft.AspNetCore.DataProtection;
using FitEdu.Application.Common;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class EncryptedStringConverterTests
{
    private const string Purpose = "test-purpose";
    private const string Answers = """{"q1":"SECRET_MARKER_answer"}""";

    private static IDataProtector NewProtector() =>
        new EphemeralDataProtectionProvider().CreateProtector(Purpose);

    private static string Read(EncryptedStringConverter converter, string stored) =>
        (string)converter.ConvertFromProvider(stored)!;

    [Fact]
    public void RoundTrip_WithTheSameKey_ReturnsThePlaintext()
    {
        var protector = NewProtector();
        var converter = new EncryptedStringConverter(protector);

        var stored = (string)converter.ConvertToProvider(Answers)!;

        stored.ShouldNotContain("SECRET_MARKER");
        Read(converter, stored).ShouldBe(Answers);
    }

    [Fact]
    public void CiphertextFromAnotherKeyRing_ReadsAsTheUnreadableMarker_AndWarnsWithoutTheValue()
    {
        // P07-2: a lost key ring used to hand the ciphertext back as if it were the answers, silently.
        var stored = NewProtector().Protect(Answers);
        var logger = new RecordingLogger();
        var converter = new EncryptedStringConverter(NewProtector(), logger);

        var read = Read(converter, stored);

        read.ShouldBe(UnreadableAnswers.Marker);
        UnreadableAnswers.IsMarker(read).ShouldBeTrue();
        logger.Warnings.ShouldHaveSingleItem().ShouldNotContain(stored);
    }

    [Fact]
    public void LegacyPlaintextJson_ReadsAsIs_WithoutAWarning()
    {
        var logger = new RecordingLogger();
        var converter = new EncryptedStringConverter(NewProtector(), logger);

        Read(converter, Answers).ShouldBe(Answers);

        logger.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void StoredTextThatIsNeitherJsonNorReadableCiphertext_ReadsAsTheUnreadableMarker()
    {
        var converter = new EncryptedStringConverter(NewProtector(), new RecordingLogger());

        Read(converter, "CfDJ8-not-a-real-payload").ShouldBe(UnreadableAnswers.Marker);
    }
}
