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
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> options, ILogger<SmtpEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task SendEmailConfirmationAsync(string email, string confirmationLink, string language, CancellationToken cancellationToken = default) =>
        SendAsync(email, EmailTemplates.EmailConfirmation(confirmationLink, language), cancellationToken);

    public Task SendPasswordResetAsync(string email, string resetLink, string language, CancellationToken cancellationToken = default) =>
        SendAsync(email, EmailTemplates.PasswordReset(resetLink, language), cancellationToken);

    private async Task SendAsync(string to, EmailContent content, CancellationToken ct)
    {
        var message = BuildMessage(_options, to, content);

        using var client = new SmtpClient();
        try
        {
            var socketOptions = _options.Smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, socketOptions, ct);
            if (!string.IsNullOrEmpty(_options.Smtp.Username))
                await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password ?? string.Empty, ct);
            await client.SendAsync(message, ct);
        }
        finally
        {
            if (client.IsConnected)
                await client.DisconnectAsync(true, ct);
        }

        _logger.LogInformation("Email '{Subject}' sent to {To}.", content.Subject, to);
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
}
