using System.Net;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>Subject and bodies of one transactional email.</summary>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// The wording of every transactional email, shared by <see cref="SmtpEmailService"/> and the
/// development <see cref="StubEmailService"/> so both use the same subjects. English only: the
/// email services have no request language to follow.
/// </summary>
public static class EmailTemplates
{
    public const string EmailConfirmationSubject = "Welcome to FitEdu — let's get moving";
    public const string PasswordResetSubject = "Reset your FitEdu password";

    private const string HtmlSignOff = "<p>Train well,<br>The FitEdu team</p>";
    private const string TextSignOff = "Train well, the FitEdu team.";

    public static EmailContent EmailConfirmation(string confirmationLink)
    {
        var safeLink = WebUtility.HtmlEncode(confirmationLink);
        var html = "<p>Welcome to FitEdu! Your fitness courses, workouts and exercise library are ready.</p>" +
                   "<p>Confirm your email address to start your first session:</p>" +
                   $"<p><a href=\"{safeLink}\">Confirm my email</a></p>" +
                   $"<p>If the link does not work, copy this URL into your browser:<br>{safeLink}</p>" +
                   HtmlSignOff;
        var text = "Welcome to FitEdu! Your fitness courses, workouts and exercise library are ready. " +
                   $"Confirm your email to start your first session: {confirmationLink}\n\n{TextSignOff}";
        return new EmailContent(EmailConfirmationSubject, html, text);
    }

    public static EmailContent PasswordReset(string resetLink)
    {
        var safeLink = WebUtility.HtmlEncode(resetLink);
        var html = "<p>We received a request to reset the password of your FitEdu account.</p>" +
                   $"<p><a href=\"{safeLink}\">Reset my password</a></p>" +
                   "<p>If you did not ask for this, you can ignore this email and keep training as usual. " +
                   $"If the link does not work, copy this URL into your browser:<br>{safeLink}</p>" +
                   HtmlSignOff;
        var text = $"Reset your FitEdu password: {resetLink}\n\n" +
                   $"If you did not ask for this, you can ignore this email. {TextSignOff}";
        return new EmailContent(PasswordResetSubject, html, text);
    }
}
