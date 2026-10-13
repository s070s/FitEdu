using System.Globalization;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Reads the filter values that list pages keep in the address (?search=…&amp;page=2), so a link,
/// a reload or the Back button brings the same list back. A malformed value is ignored, never an error.
/// </summary>
public static class QueryValues
{
    public const int FirstPage = 1;

    /// <summary>The page number in the address; the first page for a missing, malformed or too-small value.</summary>
    public static int PageNumber(string? value)
    {
        var isNumber = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var page);
        return isNumber && page >= FirstPage ? page : FirstPage;
    }

    /// <summary>The page number to write into the address: none for the first page, so links stay short.</summary>
    public static int? PageParameter(int page) => page > FirstPage ? page : null;

    /// <summary>Trimmed text from the address, or null when it is blank.</summary>
    public static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
