using Microsoft.AspNetCore.Identity;
using FitEdu.Application.Common;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Services;

/// <summary>
/// Identity's own error texts are English only. This describer gives the errors a member can meet
/// while registering, changing a password or following an emailed link (password rules, a taken or invalid email, an expired link) in the
/// member's UI language. The error codes stay Identity's, so code can still match on them.
/// </summary>
public sealed class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), ResourceFormat.Fill(ErrorMessagesRes.IdentityPasswordTooShortFormat, length));

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), ErrorMessagesRes.IdentityPasswordRequiresDigit);

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), ErrorMessagesRes.IdentityPasswordRequiresLower);

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), ErrorMessagesRes.IdentityPasswordRequiresUpper);

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), ErrorMessagesRes.IdentityPasswordRequiresNonAlphanumeric);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), ResourceFormat.Fill(ErrorMessagesRes.IdentityPasswordRequiresUniqueCharsFormat, uniqueChars));

    public override IdentityError DuplicateEmail(string email) =>
        Error(nameof(DuplicateEmail), ResourceFormat.Fill(ErrorMessagesRes.IdentityDuplicateEmailFormat, email));

    public override IdentityError InvalidEmail(string? email) =>
        Error(nameof(InvalidEmail), ResourceFormat.Fill(ErrorMessagesRes.IdentityInvalidEmailFormat, email));

    // The user name is the email (RegistrationMappings), so its errors read like the email errors.
    public override IdentityError DuplicateUserName(string userName) =>
        Error(nameof(DuplicateUserName), ResourceFormat.Fill(ErrorMessagesRes.IdentityDuplicateEmailFormat, userName));

    public override IdentityError InvalidUserName(string? userName) =>
        Error(nameof(InvalidUserName), ResourceFormat.Fill(ErrorMessagesRes.IdentityInvalidEmailFormat, userName));

    // Shown for an expired or tampered email-confirmation or password-reset link.
    public override IdentityError InvalidToken() =>
        Error(nameof(InvalidToken), ErrorMessagesRes.IdentityInvalidToken);

    private static IdentityError Error(string code, string description) =>
        new() { Code = code, Description = description };
}
