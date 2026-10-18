using System.Text.RegularExpressions;

#pragma warning disable CA1716 // The Shared namespace is the app's component root; renaming it would break every Razor import.
namespace FitEdu.Web.Shared.Components.Assistant;

public partial class AssistantMessage
{
    private static string RemoveMarkdownMarks(string text) => MarkdownMarks().Replace(text, string.Empty);

    // Bold (**text**, __text__) and heading (## text) marks; list dashes and single asterisks stay.
    [GeneratedRegex(@"\*\*|__|^#{1,6}[ \t]+", RegexOptions.Multiline)]
    private static partial Regex MarkdownMarks();
}
