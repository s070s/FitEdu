using System.Globalization;
using System.Text;

namespace FitEdu.Application.Common;

/// <summary>
/// Fills a resource format string ("Step {0}") in the current culture.
/// </summary>
public static class ResourceFormat
{
    /// <summary>
    /// The format text depends on the UI language, so it is parsed on each call rather than cached
    /// in a static field.
    /// </summary>
    public static string Fill(string resourceFormat, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, CompositeFormat.Parse(resourceFormat), values);

    /// <summary>"1 lesson" or "3 lessons": the singular text for one, the plural format filled in otherwise.</summary>
    public static string Count(int count, string singularText, string pluralFormat) =>
        count == 1 ? singularText : Fill(pluralFormat, count);
}
