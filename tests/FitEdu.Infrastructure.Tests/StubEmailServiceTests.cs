using FitEdu.Infrastructure.ApiServices;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class StubEmailServiceTests
{
    private readonly RecordingLogger<StubEmailService> _logger = new();

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
}
