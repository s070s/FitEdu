using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Localized labels for the exercise library's metadata. The database keeps the Free Exercise DB
/// values in lowercase English ("lower back", "e-z curl bar"); each one maps to an ExerciseRes
/// key made of a prefix and the value with spaces and hyphens turned into underscores
/// (Muscle_lower_back, Equipment_e_z_curl_bar). A value with no key is shown as it is.
/// </summary>
public static class ExerciseLabels
{
    public const string MusclePrefix = "Muscle_";
    public const string EquipmentPrefix = "Equipment_";
    public const string ForcePrefix = "Force_";
    public const string MechanicPrefix = "Mechanic_";
    public const string LevelPrefix = "Level_";
    public const string CategoryPrefix = "Category_";

    private const char KeySeparator = '_';

    public static string Muscle(string? value) => Label(MusclePrefix, value);

    public static string Equipment(string? value) => Label(EquipmentPrefix, value);

    public static string Force(string? value) => Label(ForcePrefix, value);

    public static string Mechanic(string? value) => Label(MechanicPrefix, value);

    public static string Level(string? value) => Label(LevelPrefix, value);

    public static string Category(string? value) => Label(CategoryPrefix, value);

    /// <summary>The ExerciseRes key for a metadata value, e.g. ("Muscle_", "lower back") → "Muscle_lower_back".</summary>
    public static string ResourceKey(string prefix, string value)
    {
        var keyPart = value.Trim().ToLowerInvariant()
            .Replace(' ', KeySeparator)
            .Replace('-', KeySeparator);
        return prefix + keyPart;
    }

    private static string Label(string prefix, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        // ResourceManager follows the current UI culture, like the generated ExerciseRes properties.
        var label = ExerciseRes.ResourceManager.GetString(ResourceKey(prefix, value), ExerciseRes.Culture);
        return label ?? value;
    }
}
