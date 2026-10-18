using System.Text.RegularExpressions;

#pragma warning disable CA1716 // The Shared namespace is the app's component root; renaming it would break every Razor import.
namespace FitEdu.Web.Shared.Components.Assistant;

public partial class AssistantMessage
{
    private static string RemoveMarkdownMarks(string text)
    {
        var withoutLinks = MarkdownLink().Replace(text, "$1");
        return MarkdownMarks().Replace(withoutLinks, string.Empty);
    }

    // [text](url) keeps only its text: the bubble shows plain text, and the sources list carries the links.
    [GeneratedRegex(@"\[([^\]\n]+)\]\([^)\s]+\)")]
    private static partial Regex MarkdownLink();

    // Bold (**text**, __text__), heading (## text) and divider (---) marks; list dashes and single asterisks stay.
    [GeneratedRegex(@"\*\*|__|^#{1,6}[ \t]+|^[ \t]*-{3,}[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex MarkdownMarks();
}
