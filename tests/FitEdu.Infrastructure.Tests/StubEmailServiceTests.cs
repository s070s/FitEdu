using System.Globalization;
using FitEdu.Infrastructure.ApiServices;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class StubEmailServiceTests
{
    private readonly RecordingLogger<StubEmailService> _logger = new();

    private StubEmailService NewService() => new(_logger);

    public StubEmailServiceTests() => CultureInfo.CurrentUICulture = new CultureInfo("en-GB");

    [Fact]
    public async Task SendEmailConfirmation_LogsTheWelcomeSubjectAndTheLink()
    {
        await NewService().SendEmailConfirmationAsync("u@x.com", "https://confirm");

        var entry = _logger.Information.ShouldHaveSingleItem();
        entry.ShouldStartWith("STUB EMAIL", Case.Sensitive); // the README tells developers to search for this
        entry.ShouldContain("Subject: Welcome to FitEdu — let's get moving", Case.Sensitive);
        entry.ShouldContain("Link: https://confirm", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_LogsTheResetSubjectAndTheLink()
    {
        await NewService().SendPasswordResetAsync("u@x.com", "https://reset");

        var entry = _logger.Information.ShouldHaveSingleItem();
        entry.ShouldStartWith("STUB EMAIL", Case.Sensitive);
        entry.ShouldContain("Subject: Reset your FitEdu password", Case.Sensitive);
        entry.ShouldContain("Link: https://reset", Case.Sensitive);
    }

    [Fact]
    public async Task SendEmailConfirmation_InGreek_LogsTheGreekSubject()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");

        await NewService().SendEmailConfirmationAsync("u@x.com", "https://confirm");

        _logger.Information.ShouldHaveSingleItem().ShouldContain("Subject: Καλώς ήρθες στο FitEdu — ώρα για κίνηση", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_InGreek_LogsTheGreekSubject()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el-GR");

        await NewService().SendPasswordResetAsync("u@x.com", "https://reset");

        _logger.Information.ShouldHaveSingleItem().ShouldContain("Subject: Επαναφορά του κωδικού σου στο FitEdu", Case.Sensitive);
    }
}
