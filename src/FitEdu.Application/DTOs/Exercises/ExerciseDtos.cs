namespace FitEdu.Application.DTOs;

/// <summary>
/// Paging default shared by the exercise library endpoint and its consumer
/// (24 fills a grid of 2, 3 or 4 columns).
/// </summary>
public static class ExerciseLibraryPaging
{
    public const int DefaultPageSize = 24;
}

/// <summary>
/// Exercise card for the library list and the admin exercise picker.
/// </summary>
public record ExerciseListItemDto(
    Guid Id,
    string Slug,
    string Name,
    string Category,
    string Level,
    string? Equipment,
    string[] PrimaryMuscles,
    string Image0Url,
    string Image1Url
);

/// <summary>
/// Full exercise page: the list fields plus technique details and the step-by-step instructions.
/// </summary>
public record ExerciseDetailDto(
    Guid Id,
    string Slug,
    string Name,
    string Category,
    string Level,
    string? Equipment,
    string[] PrimaryMuscles,
    string Image0Url,
    string Image1Url,
    string[] SecondaryMuscles,
    string? Force,
    string? Mechanic,
    string[] Instructions
);

/// <summary>
/// Optional library filters. Muscle, Equipment, Level and Category take the exact values the
/// facets endpoint returns; Search matches part of the English or Greek name.
/// </summary>
public record ExerciseFilterDto(
    string? Search = null,
    string? Muscle = null,
    string? Equipment = null,
    string? Level = null,
    string? Category = null
);

/// <summary>
/// One filter value with the number of published exercises that have it.
/// </summary>
public record ExerciseFacetDto(string Value, int Count);

/// <summary>
/// Every filter value of the library, for the filter chips.
/// </summary>
public record ExerciseFacetsDto(
    IReadOnlyList<ExerciseFacetDto> Muscles,
    IReadOnlyList<ExerciseFacetDto> Equipment,
    IReadOnlyList<ExerciseFacetDto> Levels,
    IReadOnlyList<ExerciseFacetDto> Categories)
{
    public static ExerciseFacetsDto Empty { get; } = new([], [], [], []);
}

/// <summary>
/// An exercise as prescribed inside a lesson (sets, reps, rest and coaching notes).
/// </summary>
public record LessonExerciseDto(
    Guid ExerciseId,
    string Slug,
    string Name,
    string? Equipment,
    string[] PrimaryMuscles,
    int? Sets,
    string? Reps,
    int? RestSeconds,
    string? Notes,
    string Image0Url,
    string Image1Url,
    string[] Instructions
);

/// <summary>
/// A lesson of a published course that prescribes a given exercise ("used in" on the exercise page).
/// </summary>
public record LessonUsingExerciseDto(
    Guid LessonId,
    string LessonTitle,
    Guid CourseId,
    string CourseTitle
);
