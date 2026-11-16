using System.Net;
using FitEdu.Domain.Identity;

namespace FitEdu.Infrastructure.ApiServices;

/// <summary>Subject and bodies of one transactional email.</summary>
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);

/// <summary>
/// The wording of every transactional email, in English and Greek, shared by
/// <see cref="SmtpEmailService"/> and the development <see cref="StubEmailService"/> so both use the
/// same subjects. The caller picks the language: the request language when the member asks for the
/// email themselves (signing up, forgetting a password on the Greek site), and the member's stored
/// <see cref="ApplicationUser.PreferredLanguage"/> when an admin triggers it, so a Greek member gets
/// the Greek email even when the admin reads the site in English.
/// </summary>
public static class EmailTemplates
{
    public const string EmailConfirmationSubject = "Welcome to FitEdu — let's get moving";
    public const string PasswordResetSubject = "Reset your FitEdu password";
    public const string GreekEmailConfirmationSubject = "Καλώς ήρθες στο FitEdu — ώρα για κίνηση";
    public const string GreekPasswordResetSubject = "Επαναφορά του κωδικού πρόσβασής σου στο FitEdu";

    public const string PasswordChangedByAdminSubject = "Your FitEdu password was changed";
    public const string GreekPasswordChangedByAdminSubject = "Ο κωδικός πρόσβασής σου στο FitEdu άλλαξε";

    private const string HtmlSignOff = "<p>Train well,<br>The FitEdu team</p>";
    private const string TextSignOff = "Train well, the FitEdu team.";
    private const string GreekHtmlSignOff = "<p>Καλή προπόνηση,<br>Η ομάδα του FitEdu</p>";
    private const string GreekTextSignOff = "Καλή προπόνηση, η ομάδα του FitEdu.";

    /// <summary>How long the emailed confirmation and reset links work, in hours. The emails state it and
    /// the token providers expire the links after it (set in the startup code from this one constant);
    /// 24 hours is Identity's own default, so stating it changes no behaviour.</summary>
    public const int LinkLifetimeHours = 24;

    private static readonly string LifetimeLine = $"This link works for {LinkLifetimeHours} hours.";
    private static readonly string GreekLifetimeLine = $"Ο σύνδεσμος ισχύει για {LinkLifetimeHours} ώρες.";
    private const string ConfirmationIgnoreLine = "If you didn't create a FitEdu account, you can ignore this email.";
    private const string GreekConfirmationIgnoreLine = "Αν δεν δημιούργησες εσύ λογαριασμό στο FitEdu, αγνόησε αυτό το email.";
    private const string ResetIgnoreLine = "If you didn't ask to reset your password, you can ignore this email.";
    private const string GreekResetIgnoreLine = "Αν δεν ζήτησες εσύ αλλαγή κωδικού, αγνόησε αυτό το email.";

    /// <summary>The welcome email with the confirmation link: Greek for "el", English otherwise.</summary>
    public static EmailContent EmailConfirmation(string confirmationLink, string language) =>
        IsGreek(language) ? GreekEmailConfirmation(confirmationLink) : EnglishEmailConfirmation(confirmationLink);

    /// <summary>The password-reset email with the reset link: Greek for "el", English otherwise.</summary>
    public static EmailContent PasswordReset(string resetLink, string language) =>
        IsGreek(language) ? GreekPasswordReset(resetLink) : EnglishPasswordReset(resetLink);

    /// <summary>
    /// The notice that an admin set a new password: Greek for "el", English otherwise. It has no link and
    /// never the password itself; the admin hands the new password over in person.
    /// </summary>
    public static EmailContent PasswordChangedByAdmin(string language) =>
        IsGreek(language) ? GreekPasswordChangedByAdmin() : EnglishPasswordChangedByAdmin();

    private static bool IsGreek(string language) => language == UserLanguage.Greek;

    // The HTML part is a whole document with the language on it, so a screen reader reads Greek as Greek.
    private static string HtmlDocument(string languageCode, string body) =>
        $"<html lang=\"{languageCode}\"><body>{body}</body></html>";

    private static EmailContent EnglishEmailConfirmation(string confirmationLink)
    {
        var safeLink = WebUtility.HtmlEncode(confirmationLink);
        var html = HtmlDocument("en",
                   "<p>Welcome to FitEdu! Your training programmes, workouts and exercise library are ready.</p>" +
                   "<p>Confirm your email address to start your first workout:</p>" +
                   $"<p><a href=\"{safeLink}\">Confirm my email</a></p>" +
                   $"<p>If the link does not work, copy this URL into your browser:<br>{safeLink}</p>" +
                   $"<p>{LifetimeLine} {ConfirmationIgnoreLine}</p>" +
                   HtmlSignOff);
        var text = "Welcome to FitEdu! Your training programmes, workouts and exercise library are ready. " +
                   $"Confirm your email to start your first workout: {confirmationLink}\n\n" +
                   $"{LifetimeLine} {ConfirmationIgnoreLine}\n\n{TextSignOff}";
        return new EmailContent(EmailConfirmationSubject, html, text);
    }

    private static EmailContent GreekEmailConfirmation(string confirmationLink)
    {
        var safeLink = WebUtility.HtmlEncode(confirmationLink);
        var html = HtmlDocument("el",
                   "<p>Καλώς ήρθες στο FitEdu! Τα προγράμματα, οι προπονήσεις και η βιβλιοθήκη ασκήσεων σε περιμένουν.</p>" +
                   "<p>Επιβεβαίωσε το email σου για να ξεκινήσεις την πρώτη σου προπόνηση:</p>" +
                   $"<p><a href=\"{safeLink}\">Επιβεβαίωση email</a></p>" +
                   $"<p>Αν ο σύνδεσμος δεν ανοίγει, αντέγραψε αυτή τη διεύθυνση στο πρόγραμμα περιήγησής σου:<br>{safeLink}</p>" +
                   $"<p>{GreekLifetimeLine} {GreekConfirmationIgnoreLine}</p>" +
                   GreekHtmlSignOff);
        var text = "Καλώς ήρθες στο FitEdu! Τα προγράμματα, οι προπονήσεις και η βιβλιοθήκη ασκήσεων σε περιμένουν. " +
                   $"Επιβεβαίωσε το email σου για να ξεκινήσεις την πρώτη σου προπόνηση: {confirmationLink}\n\n" +
                   $"{GreekLifetimeLine} {GreekConfirmationIgnoreLine}\n\n{GreekTextSignOff}";
        return new EmailContent(GreekEmailConfirmationSubject, html, text);
    }

    private static EmailContent EnglishPasswordReset(string resetLink)
    {
        var safeLink = WebUtility.HtmlEncode(resetLink);
        var html = HtmlDocument("en",
                   "<p>We received a request to reset the password of your FitEdu account.</p>" +
                   $"<p><a href=\"{safeLink}\">Reset my password</a></p>" +
                   $"<p>If the link does not work, copy this URL into your browser:<br>{safeLink}</p>" +
                   $"<p>{LifetimeLine} {ResetIgnoreLine}</p>" +
                   HtmlSignOff);
        var text = $"Reset your FitEdu password: {resetLink}\n\n" +
                   $"{LifetimeLine} {ResetIgnoreLine}\n\n{TextSignOff}";
        return new EmailContent(PasswordResetSubject, html, text);
    }

    private static EmailContent GreekPasswordReset(string resetLink)
    {
        var safeLink = WebUtility.HtmlEncode(resetLink);
        var html = HtmlDocument("el",
                   "<p>Λάβαμε ένα αίτημα για επαναφορά του κωδικού πρόσβασης του λογαριασμού σου στο FitEdu.</p>" +
                   $"<p><a href=\"{safeLink}\">Επαναφορά κωδικού πρόσβασης</a></p>" +
                   $"<p>Αν ο σύνδεσμος δεν ανοίγει, αντέγραψε αυτή τη διεύθυνση στο πρόγραμμα περιήγησής σου:<br>{safeLink}</p>" +
                   $"<p>{GreekLifetimeLine} {GreekResetIgnoreLine}</p>" +
                   GreekHtmlSignOff);
        var text = $"Επαναφορά του κωδικού πρόσβασής σου στο FitEdu: {resetLink}\n\n" +
                   $"{GreekLifetimeLine} {GreekResetIgnoreLine}\n\n{GreekTextSignOff}";
        return new EmailContent(GreekPasswordResetSubject, html, text);
    }

    private static EmailContent EnglishPasswordChangedByAdmin()
    {
        const string changedLine = "A FitEdu admin has set a new password for your account.";
        const string effectLine = "Your old password no longer works, and every device that was signed in has been signed out. " +
                                  "Log in with the new password you were given, and change it on your Profile page if you like.";
        const string unexpectedLine = "If you were not expecting this change, please contact the FitEdu team.";
        var html = HtmlDocument("en",
                   $"<p>{changedLine}</p>" +
                   $"<p>{effectLine}</p>" +
                   $"<p>{unexpectedLine}</p>" +
                   HtmlSignOff);
        var text = $"{changedLine}\n\n{effectLine}\n\n{unexpectedLine}\n\n{TextSignOff}";
        return new EmailContent(PasswordChangedByAdminSubject, html, text);
    }

    private static EmailContent GreekPasswordChangedByAdmin()
    {
        const string changedLine = "Ένας διαχειριστής του FitEdu όρισε νέο κωδικό πρόσβασης για τον λογαριασμό σου.";
        const string effectLine = "Ο παλιός κωδικός δεν ισχύει πια και κάθε συσκευή που ήταν συνδεδεμένη αποσυνδέθηκε. " +
                                  "Συνδέσου με τον νέο κωδικό που σου δόθηκε και, αν θέλεις, άλλαξέ τον από τη σελίδα Προφίλ.";
        const string unexpectedLine = "Αν δεν περίμενες αυτή την αλλαγή, επικοινώνησε με την ομάδα του FitEdu.";
        var html = HtmlDocument("el",
                   $"<p>{changedLine}</p>" +
                   $"<p>{effectLine}</p>" +
                   $"<p>{unexpectedLine}</p>" +
                   GreekHtmlSignOff);
        var text = $"{changedLine}\n\n{effectLine}\n\n{unexpectedLine}\n\n{GreekTextSignOff}";
        return new EmailContent(GreekPasswordChangedByAdminSubject, html, text);
    }
}
