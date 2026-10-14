using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.Common;

/// <summary>
/// Stores a member's <see cref="ApplicationUser.PreferredLanguage"/>. Shared by the language switch
/// (ProfileService) and the self-service password reset (AuthApiService), which both save the
/// language the member is reading the site in.
/// </summary>
public static partial class PreferredLanguageSaver
{
    /// <summary>
    /// Saves <paramref name="language"/> when it differs from the stored one. True when the language is
    /// stored afterwards; a failed save is logged and returns false, so the caller can carry on.
    /// </summary>
    public static async Task<bool> SaveAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, string language, ILogger logger)
    {
        if (user.PreferredLanguage == language)
            return true;

        user.PreferredLanguage = language;
        var result = await userManager.UpdateAsync(user);
        if (result.Succeeded)
            return true;

        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
        LogSaveFailed(logger, user.Id, errors);
        return false;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the preferred language of user {UserId}: {Errors}")]
    private static partial void LogSaveFailed(ILogger logger, string userId, string errors);
}
