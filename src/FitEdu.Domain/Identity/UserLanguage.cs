using System.Globalization;

namespace FitEdu.Domain.Identity;

/// <summary>
/// The two languages a member can read FitEdu in, stored on <see cref="ApplicationUser.PreferredLanguage"/>
/// as a two-letter code. Emails sent to a member when someone else starts them (an admin forcing a
/// password reset) use this stored language instead of the sender's.
/// </summary>
public static class UserLanguage
{
    public const string English = "en";
    public const string Greek = "el";

    /// <summary>Length of the stored column.</summary>
    public const int MaxLength = 5;

    /// <summary>Request validation pattern: exactly "en" or "el".</summary>
    public const string ValidationPattern = "^(en|el)$";

    // The cultures the site runs in (see the request-localization setup in the web project).
    private const string EnglishCultureName = "en-GB";
    private const string GreekCultureName = "el-GR";

    /// <summary>Greek for any Greek culture; English, the site's default, for every other culture.</summary>
    public static string FromCulture(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return culture.TwoLetterISOLanguageName == Greek ? Greek : English;
    }

    /// <summary>The language of a culture name such as "el-GR"; English when the name is empty or not Greek.</summary>
    public static string FromCultureName(string? cultureName)
    {
        if (string.IsNullOrWhiteSpace(cultureName))
            return English;

        var languagePart = cultureName.Split('-')[0];
        return string.Equals(languagePart, Greek, StringComparison.OrdinalIgnoreCase) ? Greek : English;
    }

    /// <summary>The site culture for a stored language ("el" is el-GR, anything else en-GB).</summary>
    public static string ToCultureName(string? language) =>
        language == Greek ? GreekCultureName : EnglishCultureName;

    /// <summary>True only for the two stored codes, "en" and "el".</summary>
    public static bool IsSupported(string? language) =>
        language is English or Greek;
}
