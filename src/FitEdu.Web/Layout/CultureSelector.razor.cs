using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace FitEdu.Web.Layout;

public partial class CultureSelector
{
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    /// <summary>True when the supplied two-letter code matches the active UI culture.</summary>
    private static bool IsActive(string twoLetterCode) =>
        string.Equals(
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
            twoLetterCode,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The culture-switch address that sets the language cookie (and the signed-in member's stored
    /// language) and then returns to <paramref name="returnUri"/>. Open it with forceLoad: true.
    /// </summary>
    public static string SwitchUrl(string culture, string returnUri) =>
        $"/culture/set?culture={culture}&returnUrl={Uri.EscapeDataString(returnUri)}";

    private void SetCulture(string culture) =>
        Navigation.NavigateTo(SwitchUrl(culture, Navigation.Uri), forceLoad: true);
}
