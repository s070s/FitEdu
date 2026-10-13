using System.Text;
using System.Text.RegularExpressions;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Turns a lesson's stored text into HTML. Text saved by the admin WYSIWYG editor is already HTML
/// and passes through unchanged. Seed and plain-text lessons use a small Markdown subset: "#", "##"
/// and "###" headings, "- " bullets, "1. " numbered steps, **bold** and plain paragraphs.
/// </summary>
public static partial class LessonMarkdown
{
    private const string TitleHeadingPrefix = "# ";
    private const string BulletPrefix = "- ";
    private const string BulletListTag = "ul";
    private const string NumberedListTag = "ol";

    private enum LineKind
    {
        Blank,
        Heading1,
        Heading2,
        Heading3,
        Bullet,
        Numbered,
        Paragraph
    }

    /// <summary>
    /// The lesson text as HTML. When Markdown text opens with a "# " heading equal to
    /// <paramref name="pageTitle"/>, that heading is left out: the page already shows the title as its h1.
    /// </summary>
    public static string Render(string? content, string? pageTitle = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        if (content.TrimStart().StartsWith('<'))
            return content;

        return RenderMarkdown(WithoutTitleHeading(content, pageTitle));
    }

    private static string RenderMarkdown(string markdown)
    {
        var html = new StringBuilder();
        string? openListTag = null;

        foreach (var rawLine in markdown.Split('\n'))
        {
            var line = rawLine.Trim();
            var kind = Classify(line);
            openListTag = SwitchList(html, openListTag, ListTagFor(kind));
            html.Append(RenderLine(kind, line));
        }

        CloseList(html, openListTag);
        return html.ToString();
    }

    private static LineKind Classify(string line)
    {
        if (line.Length == 0)
            return LineKind.Blank;
        if (line.StartsWith("### ", StringComparison.Ordinal))
            return LineKind.Heading3;
        if (line.StartsWith("## ", StringComparison.Ordinal))
            return LineKind.Heading2;
        if (line.StartsWith(TitleHeadingPrefix, StringComparison.Ordinal))
            return LineKind.Heading1;
        if (line.StartsWith(BulletPrefix, StringComparison.Ordinal))
            return LineKind.Bullet;
        if (NumberedItemPrefixRegex().IsMatch(line))
            return LineKind.Numbered;
        return LineKind.Paragraph;
    }

    private static string? ListTagFor(LineKind kind) => kind switch
    {
        LineKind.Bullet => BulletListTag,
        LineKind.Numbered => NumberedListTag,
        _ => null
    };

    /// <summary>
    /// Keeps the open list when the next line belongs to it; otherwise closes it with its own tag
    /// (so a numbered list ends with &lt;/ol&gt;) and opens the list the next line needs, if any.
    /// </summary>
    private static string? SwitchList(StringBuilder html, string? openListTag, string? neededListTag)
    {
        if (openListTag == neededListTag)
            return openListTag;

        CloseList(html, openListTag);
        if (neededListTag is not null)
            html.Append('<').Append(neededListTag).Append('>');

        return neededListTag;
    }

    private static void CloseList(StringBuilder html, string? openListTag)
    {
        if (openListTag is not null)
            html.Append("</").Append(openListTag).Append('>');
    }

    // A blank line only ends a list: paragraph margins already space the blocks.
    private static string RenderLine(LineKind kind, string line) => kind switch
    {
        LineKind.Heading1 => $"<h1>{line[2..]}</h1>",
        LineKind.Heading2 => $"<h2>{line[3..]}</h2>",
        LineKind.Heading3 => $"<h3>{line[4..]}</h3>",
        LineKind.Bullet => $"<li>{ProcessInline(line[BulletPrefix.Length..])}</li>",
        LineKind.Numbered => $"<li>{ProcessInline(NumberedItemPrefixRegex().Replace(line, string.Empty, 1))}</li>",
        LineKind.Paragraph => $"<p>{ProcessInline(line)}</p>",
        _ => string.Empty
    };

    private static string ProcessInline(string text) =>
        BoldRegex().Replace(text, "<strong>$1</strong>");

    /// <summary>The text without its first line when that line is the "# " heading of the page title.</summary>
    private static string WithoutTitleHeading(string markdown, string? pageTitle)
    {
        if (string.IsNullOrWhiteSpace(pageTitle))
            return markdown;

        var text = markdown.TrimStart();
        var firstLineEnd = text.IndexOf('\n');
        var firstLine = (firstLineEnd < 0 ? text : text[..firstLineEnd]).Trim();
        if (!IsHeadingOf(firstLine, pageTitle))
            return markdown;

        return firstLineEnd < 0 ? string.Empty : text[(firstLineEnd + 1)..];
    }

    private static bool IsHeadingOf(string line, string title) =>
        line.StartsWith(TitleHeadingPrefix, StringComparison.Ordinal)
        && string.Equals(line[TitleHeadingPrefix.Length..].Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase);

    // "1. ", "12. ": up to three digits, a dot and a space ("1.5 kg" stays a paragraph).
    [GeneratedRegex(@"^\d{1,3}\.\s+", RegexOptions.CultureInvariant)]
    private static partial Regex NumberedItemPrefixRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldRegex();
}
