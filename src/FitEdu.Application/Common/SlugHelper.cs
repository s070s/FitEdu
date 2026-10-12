using System.Text.RegularExpressions;

namespace FitEdu.Application.Common;

/// <summary>
/// Builds URL-safe slugs. docs/assets/exercise-slugs.txt and the exercise image folders follow
/// the same rule, so a slug always matches its image folder name.
/// </summary>
public static partial class SlugHelper
{
    private const char Separator = '-';

    /// <summary>
    /// Turns a Free Exercise DB id into a slug: lower-case it, replace every run of characters
    /// other than a-z and 0-9 with one dash, and trim dashes from both ends
    /// (e.g. "3_4_Sit-Up" becomes "3-4-sit-up"). Returns an empty string when nothing is left.
    /// </summary>
    public static string FromExerciseId(string? exerciseId)
    {
        if (string.IsNullOrWhiteSpace(exerciseId))
            return string.Empty;

        var lowerCaseId = exerciseId.ToLowerInvariant();
        var dashedId = NonSlugCharactersRegex().Replace(lowerCaseId, Separator.ToString());
        return dashedId.Trim(Separator);
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlugCharactersRegex();
}
