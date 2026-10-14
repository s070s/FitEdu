namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Abstraction for sending transactional emails.
/// NOTE
/// Only confirmation and password-reset messages exist today.
/// A production build would likely add welcome, payment-receipt, subscription-change, and
/// course-completion notifications here.
/// Every message takes the language to write it in ("en" or "el", see UserLanguage): the caller
/// passes the request language when the member asks for the email themselves, and the member's stored
/// language when someone else (an admin) triggers it.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email confirmation link to a user.
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="confirmationLink">Email confirmation URL</param>
    /// <param name="language">Language of the email: "el" is Greek, anything else English</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendEmailConfirmationAsync(string email, string confirmationLink, string language, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a password reset link to a user.
    /// </summary>
    /// <param name="email">User's email address</param>
    /// <param name="resetLink">Password reset URL</param>
    /// <param name="language">Language of the email: "el" is Greek, anything else English</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task SendPasswordResetAsync(string email, string resetLink, string language, CancellationToken cancellationToken = default);
}
