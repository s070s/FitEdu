using System.Globalization;
using System.Net;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>Subject and bodies of one transactional email.</summary>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// The wording of every transactional email, in English and Greek, shared by
/// <see cref="SmtpEmailService"/> and the development <see cref="StubEmailService"/> so both use the
/// same subjects. The language is the UI culture of the request that sends the email: the page's
/// loopback API call carries the member's language (Accept-Language), so a member who signs up or
/// asks for a reset on the Greek site gets the Greek email.
/// </summary>
public static class EmailTemplates
{
    public const string EmailConfirmationSubject = "Welcome to FitEdu — let's get moving";
    public const string PasswordResetSubject = "Reset your FitEdu password";
    public const string GreekEmailConfirmationSubject = "Καλώς ήρθες στο FitEdu — ώρα για κίνηση";
    public const string GreekPasswordResetSubject = "Επαναφορά του κωδικού σου στο FitEdu";

    private const string GreekLanguageCode = "el";

    private const string HtmlSignOff = "<p>Train well,<br>The FitEdu team</p>";
    private const string TextSignOff = "Train well, the FitEdu team.";
    private const string GreekHtmlSignOff = "<p>Καλή προπόνηση,<br>Η ομάδα του FitEdu</p>";
    private const string GreekTextSignOff = "Καλή προπόνηση, η ομάδα του FitEdu.";

    /// <summary>The welcome email with the confirmation link, in the current UI language.</summary>
    public static EmailContent EmailConfirmation(string confirmationLink) =>
        IsGreek() ? GreekEmailConfirmation(confirmationLink) : EnglishEmailConfirmation(confirmationLink);

    /// <summary>The password-reset email with the reset link, in the current UI language.</summary>
    public static EmailContent PasswordReset(string resetLink) =>
        IsGreek() ? GreekPasswordReset(resetLink) : EnglishPasswordReset(resetLink);

    private static bool IsGreek() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == GreekLanguageCode;

    private static EmailContent EnglishEmailConfirmation(string confirmationLink)
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

    private static EmailContent GreekEmailConfirmation(string confirmationLink)
    {
        var safeLink = WebUtility.HtmlEncode(confirmationLink);
        var html = "<p>Καλώς ήρθες στο FitEdu! Τα προγράμματα, οι προπονήσεις και η βιβλιοθήκη ασκήσεων σε περιμένουν.</p>" +
                   "<p>Επιβεβαίωσε το email σου για να ξεκινήσεις την πρώτη σου προπόνηση:</p>" +
                   $"<p><a href=\"{safeLink}\">Επιβεβαίωση email</a></p>" +
                   $"<p>Αν ο σύνδεσμος δεν ανοίγει, αντέγραψε αυτή τη διεύθυνση στο πρόγραμμα περιήγησής σου:<br>{safeLink}</p>" +
                   GreekHtmlSignOff;
        var text = "Καλώς ήρθες στο FitEdu! Τα προγράμματα, οι προπονήσεις και η βιβλιοθήκη ασκήσεων σε περιμένουν. " +
                   $"Επιβεβαίωσε το email σου για να ξεκινήσεις την πρώτη σου προπόνηση: {confirmationLink}\n\n{GreekTextSignOff}";
        return new EmailContent(GreekEmailConfirmationSubject, html, text);
    }

    private static EmailContent EnglishPasswordReset(string resetLink)
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

    private static EmailContent GreekPasswordReset(string resetLink)
    {
        var safeLink = WebUtility.HtmlEncode(resetLink);
        var html = "<p>Λάβαμε ένα αίτημα για επαναφορά του κωδικού του λογαριασμού σου στο FitEdu.</p>" +
                   $"<p><a href=\"{safeLink}\">Επαναφορά κωδικού</a></p>" +
                   "<p>Αν δεν το ζήτησες εσύ, αγνόησε αυτό το email και συνέχισε την προπόνηση κανονικά. " +
                   $"Αν ο σύνδεσμος δεν ανοίγει, αντέγραψε αυτή τη διεύθυνση στο πρόγραμμα περιήγησής σου:<br>{safeLink}</p>" +
                   GreekHtmlSignOff;
        var text = $"Επαναφορά του κωδικού σου στο FitEdu: {resetLink}\n\n" +
                   $"Αν δεν το ζήτησες εσύ, αγνόησε αυτό το email. {GreekTextSignOff}";
        return new EmailContent(GreekPasswordResetSubject, html, text);
    }
}
