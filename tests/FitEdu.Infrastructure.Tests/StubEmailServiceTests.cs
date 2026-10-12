using Microsoft.Extensions.Logging;
using FitEdu.Infrastructure.ApiServices;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

public class StubEmailServiceTests
{
    private readonly InformationLogger _logger = new();

    private StubEmailService NewService() => new(_logger);

    [Fact]
    public async Task SendEmailConfirmation_LogsTheWelcomeSubjectAndTheLink()
    {
        await NewService().SendEmailConfirmationAsync("u@x.com", "https://confirm");

        var entry = _logger.Messages.ShouldHaveSingleItem();
        entry.ShouldStartWith("STUB EMAIL", Case.Sensitive); // the README tells developers to search for this
        entry.ShouldContain("Subject: Welcome to FitEdu — let's get moving", Case.Sensitive);
        entry.ShouldContain("Link: https://confirm", Case.Sensitive);
    }

    [Fact]
    public async Task SendPasswordReset_LogsTheResetSubjectAndTheLink()
    {
        await NewService().SendPasswordResetAsync("u@x.com", "https://reset");

        var entry = _logger.Messages.ShouldHaveSingleItem();
        entry.ShouldStartWith("STUB EMAIL", Case.Sensitive);
        entry.ShouldContain("Subject: Reset your FitEdu password", Case.Sensitive);
        entry.ShouldContain("Link: https://reset", Case.Sensitive);
    }

    /// <summary>Keeps the text of every information entry the stub writes.</summary>
    private sealed class InformationLogger : ILogger<StubEmailService>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Information)
                Messages.Add(formatter(state, exception));
        }
    }
}
