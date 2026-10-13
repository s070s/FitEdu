namespace FitEdu.Application.DTOs;

/// <summary>
/// One row of the Free Exercise DB file (exercises.json), in the dataset's own field names.
/// No property is <c>required</c>: a row that lacks one is skipped on its own by the seeder,
/// instead of the serializer refusing the whole file.
/// </summary>
public sealed class ExerciseSeedDto
{
    /// <summary>Dataset id (e.g. "3_4_Sit-Up"); the seeder turns it into the slug.</summary>
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Force { get; init; }
    public string Level { get; init; } = string.Empty;
    public string? Mechanic { get; init; }
    public string? Equipment { get; init; }
    public List<string> PrimaryMuscles { get; init; } = [];
    public List<string> SecondaryMuscles { get; init; } = [];
    public List<string> Instructions { get; init; } = [];
    public string Category { get; init; } = string.Empty;
    public List<string> Images { get; init; } = [];
}

/// <summary>
/// One Greek translation row (exercises.el.json or el/*.el.json), matched to an exercise by slug.
/// </summary>
public sealed class ExerciseGreekSeedDto
{
    public string Slug { get; init; } = string.Empty;
    public string? NameEl { get; init; }
    public List<string> InstructionsEl { get; init; } = [];
}
