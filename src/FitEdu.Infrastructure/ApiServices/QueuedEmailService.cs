using FitEdu.Application.ApiInterfaces;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// The <see cref="IEmailService"/> the app uses when an SMTP relay is configured: it puts the email in the
/// <see cref="EmailOutbox"/> and returns at once. <see cref="EmailDispatcher"/> does the slow part, so a
/// slow or unreachable relay never holds up a sign-up or a password-reset request.
/// </summary>
public sealed class QueuedEmailService(EmailOutbox queue) : IEmailService
{
    public Task SendEmailConfirmationAsync(string email, string confirmationLink, string language, CancellationToken cancellationToken = default) =>
        Enqueue(email, EmailTemplates.EmailConfirmation(confirmationLink, language));

    public Task SendPasswordResetAsync(string email, string resetLink, string language, CancellationToken cancellationToken = default) =>
        Enqueue(email, EmailTemplates.PasswordReset(resetLink, language));

    // A full line throws, like a failed send used to: the callers already catch and log that.
    private Task Enqueue(string to, EmailContent content)
    {
        return queue.TryEnqueue(new QueuedEmail(to, content))
            ? Task.CompletedTask
            : Task.FromException(new InvalidOperationException("The email queue is full; the email was not queued."));
    }
}
