using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.ApiServices;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public sealed class StubEmailServiceTests : IDisposable
{
    private readonly RecordingLogger<StubEmailService> _logger = new();

    // The sender runs in English; the email language is the one passed in. Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new("en-GB");

    private StubEmailService NewService() => new(_logger);

    [Fact]
    public async Task SendEmailConfirmation_LogsTheWelcomeSubjectAndTheLink()
    {
        await NewService().SendEmailConfirmationAsync("u@x.com", "https://confirm", UserLanguage.English);

        var entry = _logger.Information.ShouldHaveSingleItem();
        entry.ShouldStartWith("STUB EMAIL", Case.Sensitive); // the README tells developers to search for this
        entry.ShouldContain("Subject: Welcome to FitEdu — let's get moving", Case.Sensitive);
        entry.ShouldContain("Link: https://confirm", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_LogsTheResetSubjectAndTheLink()
    {
        await NewService().SendPasswordResetAsync("u@x.com", "https://reset", UserLanguage.English);

        var entry = _logger.Information.ShouldHaveSingleItem();
        entry.ShouldStartWith("STUB EMAIL", Case.Sensitive);
        entry.ShouldContain("Subject: Reset your FitEdu password", Case.Sensitive);
        entry.ShouldContain("Link: https://reset", Case.Sensitive);
    }

    [Fact]
    public async Task SendEmailConfirmation_InGreek_LogsTheGreekSubject()
    {
        await NewService().SendEmailConfirmationAsync("u@x.com", "https://confirm", UserLanguage.Greek);

        _logger.Information.ShouldHaveSingleItem().ShouldContain("Subject: Καλώς ήρθες στο FitEdu — ώρα για κίνηση", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_InGreek_LogsTheGreekSubject()
    {
        await NewService().SendPasswordResetAsync("u@x.com", "https://reset", UserLanguage.Greek);

        _logger.Information.ShouldHaveSingleItem().ShouldContain("Subject: Επαναφορά του κωδικού σου στο FitEdu", Case.Sensitive);
    }

    public void Dispose() => _cultureScope.Dispose();
}
