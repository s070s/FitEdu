namespace FitEdu.Web.Extensions;

/// <summary>
/// The Bootstrap Icons glyph for a course or assessment category. The API sends the category name
/// in the page language, so the map holds the seeded English and Greek names; any other name
/// (or none) gets the general activity icon.
/// </summary>
public static class CategoryIcons
{
    public const string Fallback = "bi-activity";

    private static readonly Dictionary<string, string> IconsByCategoryName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Strength"] = "bi-lightning-charge",
        ["Δύναμη"] = "bi-lightning-charge",
        ["Cardio"] = "bi-heart-pulse",
        ["Κάρντιο"] = "bi-heart-pulse",
        ["Mobility"] = "bi-person-arms-up",
        ["Κινητικότητα"] = "bi-person-arms-up",
        ["Core"] = "bi-bullseye",
        ["Κορμός"] = "bi-bullseye",
        ["Recovery"] = "bi-moon-stars",
        ["Αποκατάσταση"] = "bi-moon-stars",
        ["Nutrition"] = "bi-egg-fried",
        ["Διατροφή"] = "bi-egg-fried",
        ["Performance"] = "bi-speedometer2",
        ["Απόδοση"] = "bi-speedometer2",
        ["Mindset"] = "bi-lightbulb",
        ["Νοοτροπία"] = "bi-lightbulb",
        ["Readiness"] = "bi-clipboard2-check",
        ["Ετοιμότητα"] = "bi-clipboard2-check",
        ["Safety"] = "bi-shield-check",
        ["Ασφάλεια"] = "bi-shield-check",
        ["Setup"] = "bi-gear",
        ["Προετοιμασία"] = "bi-gear"
    };

    public static string For(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return Fallback;

        return IconsByCategoryName.GetValueOrDefault(categoryName.Trim(), Fallback);
    }
}
