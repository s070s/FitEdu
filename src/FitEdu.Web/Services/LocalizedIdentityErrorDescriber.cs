using Microsoft.AspNetCore.Identity;
using FitEdu.Shared.Resources.Messages;
using FitEdu.Web.Extensions;

namespace FitEdu.Web.Services;

/// <summary>
/// Identity's own error texts are English only. This describer gives the errors a member can meet
/// while registering or changing a password (password rules, a taken or invalid email) in the
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

    private static IdentityError Error(string code, string description) =>
        new() { Code = code, Description = description };
}
