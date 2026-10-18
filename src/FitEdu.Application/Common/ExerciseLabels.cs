using System.Globalization;
using System.Text.RegularExpressions;
using FitEdu.Shared.Resources;

namespace FitEdu.Application.Common;

/// <summary>
/// Localized labels for the exercise library's metadata. The database keeps the Free Exercise DB
/// values in lowercase English ("lower back", "e-z curl bar"); each one maps to an ExerciseRes
/// key made of a prefix and the value with spaces and hyphens turned into underscores
/// (Muscle_lower_back, Equipment_e_z_curl_bar). A value with no key is shown as it is.
/// </summary>
public static partial class ExerciseLabels
{
    public const string MusclePrefix = "Muscle_";
    public const string EquipmentPrefix = "Equipment_";
    public const string ForcePrefix = "Force_";
    public const string MechanicPrefix = "Mechanic_";
    public const string LevelPrefix = "Level_";
    public const string CategoryPrefix = "Category_";

    private const char KeySeparator = '_';

    public static string Muscle(string? value) => LabelIn(MusclePrefix, value, ExerciseRes.Culture);

    public static string Equipment(string? value) => LabelIn(EquipmentPrefix, value, ExerciseRes.Culture);

    public static string Force(string? value) => LabelIn(ForcePrefix, value, ExerciseRes.Culture);

    public static string Mechanic(string? value) => LabelIn(MechanicPrefix, value, ExerciseRes.Culture);

    public static string Level(string? value) => LabelIn(LevelPrefix, value, ExerciseRes.Culture);

    public static string Category(string? value) => LabelIn(CategoryPrefix, value, ExerciseRes.Culture);

    /// <summary>
    /// True when a prescribed "reps" value is a time ("20 s", "30–45 s", "1 min + 1 min") rather than
    /// a count ("8–12", "10 each side"), so a workout card can label it Time instead of Reps.
    /// </summary>
    public static bool IsDuration(string? reps) =>
        !string.IsNullOrWhiteSpace(reps) && DurationRegex().IsMatch(reps.Trim());

    /// <summary>The ExerciseRes key for a metadata value, e.g. ("Muscle_", "lower back") → "Muscle_lower_back".</summary>
    public static string ResourceKey(string prefix, string value)
    {
        var keyPart = value.Trim().ToLowerInvariant()
            .Replace(' ', KeySeparator)
            .Replace('-', KeySeparator);
        return prefix + keyPart;
    }

    /// <summary>
    /// The label in a given language. A null culture follows the current UI culture, like the
    /// generated ExerciseRes properties (ExerciseRes.Culture is normally null); code that does not
    /// run in the member's UI culture (the coach's tools) passes the culture it needs.
    /// </summary>
    public static string LabelIn(string prefix, string? value, CultureInfo? culture)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var label = ExerciseRes.ResourceManager.GetString(ResourceKey(prefix, value), culture);
        return label ?? value;
    }

    // A number or a range, then a time unit: "20 s", "30-45 s", "30–35 min @ RPE 5". "15 m" (metres) is not a time.
    [GeneratedRegex(@"^\d+(\s*[-–]\s*\d+)?\s*(s|sec|min)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex DurationRegex();
}
