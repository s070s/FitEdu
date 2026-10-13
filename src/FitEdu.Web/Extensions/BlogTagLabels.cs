using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Localized labels for blog tags. Articles store English tag keywords ("Strength"); HomeRes holds
/// one label per known keyword (BlogTag_Strength, …), so Greek pages show Greek chips.
/// </summary>
public static class BlogTagLabels
{
    public const string TagPrefix = "BlogTag_";

    /// <summary>The label of one stored tag; a tag with no label (for example one an admin typed) is shown as it is.</summary>
    public static string Label(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return string.Empty;

        var trimmedTag = tag.Trim();

        // ResourceManager follows the current UI culture, like the generated HomeRes properties.
        var label = HomeRes.ResourceManager.GetString(TagPrefix + trimmedTag, HomeRes.Culture);
        return label ?? trimmedTag;
    }
}
