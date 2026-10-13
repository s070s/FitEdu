using FitEdu.Infrastructure.ApiServices;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class StubEmailServiceTests : IDisposable
{
    private readonly RecordingLogger<StubEmailService> _logger = new();

    // English unless a test says otherwise; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new("en-GB");

    private StubEmailService NewService() => new(_logger);

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
        using var cultureScope = new CultureScope("el-GR");

        await NewService().SendEmailConfirmationAsync("u@x.com", "https://confirm");

        _logger.Information.ShouldHaveSingleItem().ShouldContain("Subject: Καλώς ήρθες στο FitEdu — ώρα για κίνηση", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_InGreek_LogsTheGreekSubject()
    {
        using var cultureScope = new CultureScope("el-GR");

        await NewService().SendPasswordResetAsync("u@x.com", "https://reset");

        _logger.Information.ShouldHaveSingleItem().ShouldContain("Subject: Επαναφορά του κωδικού σου στο FitEdu", Case.Sensitive);
    }

    public void Dispose() => _cultureScope.Dispose();
}
