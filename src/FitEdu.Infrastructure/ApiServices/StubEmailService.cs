using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Stub email service for development.
/// Logs emails instead of sending them — no message is ever delivered. It is registered only in
/// Development when no SMTP host is configured (otherwise <see cref="SmtpEmailService"/> sends
/// real mail); outside Development a missing SMTP host fails fast at startup. The logged subject
/// comes from <see cref="EmailTemplates"/> in the language the caller passes (English or Greek).
///
/// LOG-2: the logged confirmation/reset links embed live Identity tokens and the recipient's
/// email — i.e. usable credentials sitting in plaintext in <c>Logs/</c> until they expire or are
/// consumed. This is a deliberate Development-only convenience (the README documents "search STUB
/// EMAIL" to grab the link); never register this service outside Development, and treat dev log
/// files as sensitive.
/// </summary>
public class StubEmailService : IEmailService
{
    private readonly ILogger<StubEmailService> _logger;

    public StubEmailService(ILogger<StubEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailConfirmationAsync(string email, string confirmationLink, string language, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "STUB EMAIL - Email Confirmation:\n" +
            "To: {Email}\n" +
            "Subject: {Subject}\n" +
            "Link: {ConfirmationLink}",
            email, EmailTemplates.EmailConfirmation(confirmationLink, language).Subject, confirmationLink);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string email, string resetLink, string language, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "STUB EMAIL - Password Reset:\n" +
            "To: {Email}\n" +
            "Subject: {Subject}\n" +
            "Link: {ResetLink}",
            email, EmailTemplates.PasswordReset(resetLink, language).Subject, resetLink);

        return Task.CompletedTask;
    }
}
