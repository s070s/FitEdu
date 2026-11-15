using MailKit.Net.Smtp;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Sends the emails of the <see cref="EmailOutbox"/>, one at a time, through <see cref="SmtpEmailService"/>
/// (resolved in its own scope, as the service is scoped). A failed send is logged with the subject and the
/// kind of error, never the address: a relay's reply can repeat the recipient. The next email still goes.
/// </summary>
public sealed partial class EmailDispatcher(EmailOutbox queue, IServiceScopeFactory scopeFactory, ILogger<EmailDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
                await SendAsync(email, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is stopping.
        }
    }

    private async Task SendAsync(QueuedEmail email, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var transport = scope.ServiceProvider.GetRequiredService<SmtpEmailService>();
            await transport.SendAsync(email.To, email.Content, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            if (ex is SmtpCommandException commandFailure)
            {
                // The relay's numeric reply and MailKit's error code say why it refused, without its text (which can name the address).
                LogSmtpCommandFailed(logger, email.Content.Subject, (int)commandFailure.StatusCode, commandFailure.ErrorCode);
                return;
            }

            LogSendFailed(logger, email.Content.Subject, ex.GetType().Name);
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Error, Message = "Email '{Subject}' could not be sent ({ErrorType}).")]
    private static partial void LogSendFailed(ILogger logger, string subject, string errorType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Email '{Subject}' was refused by the mail server (SMTP status {StatusCode}, {ErrorCode}).")]
    private static partial void LogSmtpCommandFailed(ILogger logger, string subject, int statusCode, SmtpErrorCode errorCode);
}
