using System.Globalization;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Admin text in a fixed content language, whatever the admin's own UI language. Editors that show an
/// English and a Greek column use it so each column's placeholder is in that column's language.
/// </summary>
public static class ContentLanguageText
{
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-GB");
    private static readonly CultureInfo GreekCulture = CultureInfo.GetCultureInfo("el-GR");

    /// <summary>The English value of an <see cref="AdminRes"/> key, e.g. nameof(AdminRes.OptionsPlaceholder).</summary>
    public static string English(string adminResKey) =>
        AdminRes.ResourceManager.GetString(adminResKey, EnglishCulture) ?? string.Empty;

    /// <summary>The Greek value of an <see cref="AdminRes"/> key.</summary>
    public static string Greek(string adminResKey) =>
        AdminRes.ResourceManager.GetString(adminResKey, GreekCulture) ?? string.Empty;
}
