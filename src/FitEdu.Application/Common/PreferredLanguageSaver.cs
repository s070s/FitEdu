using System.Globalization;
using Microsoft.AspNetCore.Identity;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.Common;

/// <summary>
/// Stores a member's <see cref="ApplicationUser.PreferredLanguage"/>. Shared by both sign-in paths
/// (AuthService for the site, AuthApiService for the API), which save the language the member is reading
/// the site in. Only requests the member is proven to make save it: neither the anonymous forgot-password
/// request nor the language-switch link (`/culture/set`, a plain GET any site could send them to) does.
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

    /// <summary>
    /// Saves the language of the current request (its UI culture), as at sign-in. Same result as
    /// <see cref="SaveAsync"/>.
    /// </summary>
    public static Task<bool> SaveRequestLanguageAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, ILogger logger)
    {
        var requestLanguage = UserLanguage.FromCulture(CultureInfo.CurrentUICulture);
        return SaveAsync(userManager, user, requestLanguage, logger);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not save the preferred language of user {UserId}: {Errors}")]
    private static partial void LogSaveFailed(ILogger logger, string userId, string errors);
}
