using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Infrastructure.Configuration;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>
/// Sends transactional email over SMTP using MailKit. Provider-agnostic: works against a local
/// Papercut/Mailhog catcher in development and any relay (SES, SendGrid SMTP, Mailgun, O365) in
/// production. Configured via <see cref="EmailOptions"/> (section "Email"). The wording comes from
/// <see cref="EmailTemplates"/>, in the language the caller passes (English or Greek).
/// </summary>
public sealed partial class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;
    private readonly Func<ISmtpClient> _createClient;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
        : this(options, logger, () => new SmtpClient())
    {
    }

    // The seam the tests use to hand in a fake SMTP client instead of opening a connection.
    internal SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger, Func<ISmtpClient> createClient)
    {
        _options = options.Value;
        _logger = logger;
        _createClient = createClient;
    }

    public Task SendEmailConfirmationAsync(string email, string confirmationLink, string language, CancellationToken cancellationToken = default) =>
        SendAsync(email, EmailTemplates.EmailConfirmation(confirmationLink, language), cancellationToken);

    public Task SendPasswordResetAsync(string email, string resetLink, string language, CancellationToken cancellationToken = default) =>
        SendAsync(email, EmailTemplates.PasswordReset(resetLink, language), cancellationToken);

    private async Task SendAsync(string to, EmailContent content, CancellationToken ct)
    {
        var message = BuildMessage(_options, to, content);

        // Disposing the client closes a connection a failed send left open; a close inside a finally
        // block would throw its own error over the send error that explains what went wrong.
        using var client = _createClient();
        var socketOptions = _options.Smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
        await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, socketOptions, ct);
        if (!string.IsNullOrEmpty(_options.Smtp.Username))
            await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password ?? string.Empty, ct);
        await client.SendAsync(message, ct);

        // The subject only: an address in the log is personal data kept in the log files.
        LogEmailSent(_logger, content.Subject);

        await DisconnectQuietlyAsync(client, ct);
    }

    // The message is already sent here, so a failing goodbye must not turn the send into a failure.
    private async Task DisconnectQuietlyAsync(ISmtpClient client, CancellationToken ct)
    {
        try
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDisconnectFailed(_logger, ex);
        }
    }

    /// <summary>Builds the MIME message. Extracted so message construction is unit-testable
    /// without a live SMTP server.</summary>
    public static MimeMessage BuildMessage(EmailOptions options, string to, EmailContent content)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(options.Smtp.FromName, options.Smtp.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = content.Subject;
        message.Body = new BodyBuilder { HtmlBody = content.HtmlBody, TextBody = content.TextBody }.ToMessageBody();
        return message;
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.

    [LoggerMessage(Level = LogLevel.Information, Message = "Email '{Subject}' sent.")]
    private static partial void LogEmailSent(ILogger logger, string subject);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Closing the SMTP connection failed after the email was sent.")]
    private static partial void LogDisconnectFailed(ILogger logger, Exception exception);
}
