using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.Common;

/// <summary>What the password check of an unconfirmed account found.</summary>
public enum UnconfirmedPasswordOutcome
{
    WrongPassword,
    RightPassword,
    LockedOut
}

/// <summary>
/// The password check for an account whose email is not confirmed. The sign-in manager refuses such an
/// account before it looks at the password, so its lockout never counts; this check does the same
/// lockout steps as <c>CheckPasswordSignInAsync(lockoutOnFailure: true)</c> for a confirmed account:
/// a locked-out account is refused before the password is checked, a wrong password counts as a
/// failed attempt (so guessing at an unconfirmed account ends in a lockout too), and a right password
/// resets the count.
/// </summary>
public static class UnconfirmedAccountPasswordCheck
{
    public static async Task<UnconfirmedPasswordOutcome> RunAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user,
        string password)
    {
        if (await userManager.IsLockedOutAsync(user))
            return UnconfirmedPasswordOutcome.LockedOut;

        var passwordIsRight = await userManager.CheckPasswordAsync(user, password);
        var lockoutApplies = userManager.SupportsUserLockout && await userManager.GetLockoutEnabledAsync(user);

        if (passwordIsRight)
        {
            // Like the sign-in manager for a confirmed account: a right password clears the earlier wrong guesses.
            if (lockoutApplies)
                await userManager.ResetAccessFailedCountAsync(user);
            return UnconfirmedPasswordOutcome.RightPassword;
        }

        if (!lockoutApplies)
            return UnconfirmedPasswordOutcome.WrongPassword;

        await userManager.AccessFailedAsync(user);
        return await userManager.IsLockedOutAsync(user)
            ? UnconfirmedPasswordOutcome.LockedOut
            : UnconfirmedPasswordOutcome.WrongPassword;
    }
}
