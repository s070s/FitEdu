using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace FitEdu.Application.Common;

/// <summary>
/// A multi-select assessment answer is stored as a JSON array of the ticked options, because
/// options may contain commas ("Chest pain, pressure or discomfort"). Answers stored before that
/// change are plain comma-joined text, so every reader falls back to the raw text.
/// </summary>
public static class MultiSelectAnswer
{
    /// <summary>Joins the ticked options when an answer is shown.</summary>
    public const string DisplaySeparator = " · ";

    // Keeps Greek options readable in the stored text instead of escaped code points.
    private static readonly JsonSerializerOptions StoreOptions = new()
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    /// <summary>The stored answer for the ticked options: a JSON array, or empty when none is ticked.</summary>
    public static string Serialize(IReadOnlyCollection<string> options) =>
        options.Count == 0 ? string.Empty : JsonSerializer.Serialize(options, StoreOptions);

    /// <summary>The ticked options of a JSON-array answer, or null for any other text.</summary>
    public static List<string>? TryParse(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer) || !answer.TrimStart().StartsWith('['))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(answer);
        }
        catch (JsonException)
        {
            return null; // Text that only starts with "[" is a plain answer, not a list.
        }
    }

    /// <summary>A JSON-array answer as its options joined by <see cref="DisplaySeparator"/>; any other answer as it is.</summary>
    public static string ToDisplayText(string? answer)
    {
        var options = TryParse(answer);
        if (options is null)
            return answer ?? string.Empty;

        return string.Join(DisplaySeparator, options);
    }

    /// <summary>True for a missing or blank answer, and for a JSON array with no options.</summary>
    public static bool IsBlank(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return true;

        var options = TryParse(answer);
        return options is not null && options.All(string.IsNullOrWhiteSpace);
    }
}
