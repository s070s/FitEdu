using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Application.Common;

/// <summary>
/// Identity password validator that turns away the passwords people choose most often
/// (<see cref="CommonPasswords"/>). It runs for every password Identity sets: registration, password
/// reset, change password and an admin's reset.
/// </summary>
public sealed class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    public const string ErrorCode = "PasswordTooCommon";

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        if (!CommonPasswords.IsCommon(password))
            return Task.FromResult(IdentityResult.Success);

        var error = new IdentityError { Code = ErrorCode, Description = ErrorMessagesRes.IdentityPasswordTooCommon };
        return Task.FromResult(IdentityResult.Failed(error));
    }
}
