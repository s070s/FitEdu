using System.Net;
using System.Text.RegularExpressions;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Plain text from the stored rich-text HTML (course and article bodies), for word counts and for
/// search and social descriptions, which must not contain tags.
/// </summary>
public static partial class HtmlText
{
    /// <summary>The text with every tag removed, entities such as &amp;amp; decoded and runs of spaces made one.</summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var withoutTags = HtmlTag().Replace(html, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Whitespace().Replace(decoded, " ").Trim();
    }

    /// <summary>
    /// The plain text cut to at most <paramref name="maxLength"/> characters at a word break, with
    /// "…" when it was cut; search engines show about 160 characters of a description.
    /// </summary>
    public static string Summary(string? html, int maxLength)
    {
        var text = ToPlainText(html);
        if (text.Length <= maxLength)
            return text;

        var cut = text[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 0)
            cut = cut[..lastSpace];

        return cut.TrimEnd(',', '.', ';', ':') + "…";
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
