using System.Text.Json.Serialization;

namespace FitEdu.Application.DTOs;

/// <summary>
/// Why an authentication call was refused, in <see cref="AuthResponseDto.Failure"/>. Callers (the pages)
/// switch on this, never on the translated <see cref="AuthResponseDto.Message"/>, which changes with the
/// language and the wording. The API answers exactly as much as its message does: for example it never
/// says <see cref="EmailNotConfirmed"/> or <see cref="EmailTaken"/>, so an answer cannot find accounts.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AuthFailure
{
    /// <summary>Unknown email or wrong password (one answer for both).</summary>
    InvalidCredentials,

    /// <summary>The password was right but the email address is not confirmed yet (sign-in page only).</summary>
    EmailNotConfirmed,

    /// <summary>Too many wrong passwords: the account is locked for a while.</summary>
    AccountLocked,

    /// <summary>The password was right but an admin has disabled the account.</summary>
    AccountDisabled,

    /// <summary>The page's own attempt limit was reached (sign-in, sign-up and forgotten-password pages).</summary>
    TooManyAttempts,

    /// <summary>An emailed confirmation or reset link that is unknown, used, expired or altered.</summary>
    LinkInvalid,

    /// <summary>An account with this email already exists (sign-up page only; the API hides it).</summary>
    EmailTaken,

    /// <summary>Sign-up without a date of birth.</summary>
    DateOfBirthRequired,

    /// <summary>Sign-up with a date of birth in the future or an age under the minimum sign-up age.</summary>
    UnderMinimumAge,

    /// <summary>A refresh token that is unknown, expired, already used or issued before a password change.</summary>
    InvalidRefreshToken
}
