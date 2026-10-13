using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Picks which language of a bilingual title or name the admin pages show, so a Greek admin reads
/// Greek titles in the lists instead of the English ones.
/// </summary>
public static class AdminText
{
    private const string GreekLanguageCode = "el";

    /// <summary>The Greek value under the Greek UI when one is set, otherwise the English value.</summary>
    [return: NotNullIfNotNull(nameof(english))]
    public static string? Pick(string? english, string? greek)
    {
        var isGreek = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == GreekLanguageCode;
        if (isGreek && !string.IsNullOrWhiteSpace(greek))
            return greek;

        return english;
    }
}
