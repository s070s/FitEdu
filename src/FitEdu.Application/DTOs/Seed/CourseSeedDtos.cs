namespace FitEdu.Application.DTOs;
/// <summary>
/// DTO for seeding courses from JSON files.
/// </summary>
public sealed class CourseSeedDto
{
    public required string Title { get; init; }
    public string? TitleEl { get; init; }
    public string? Description { get; init; }
    public string? DescriptionEl { get; init; }
    public string RequiredTier { get; init; } = "Free";
    public bool IsPublished { get; init; }
    public string? Category { get; init; }

    /// <summary>Greek category name; fills Category.NameEl when the category has none yet.</summary>
    public string? CategoryEl { get; init; }

    /// <summary>CourseLevel name (case-insensitive); unknown values fall back to Beginner.</summary>
    public string Level { get; init; } = "Beginner";

    /// <summary>FitnessGoal name (case-insensitive); unknown values fall back to GeneralFitness.</summary>
    public string Goal { get; init; } = "GeneralFitness";

    /// <summary>Comma-joined equipment codes (e.g. "none,mat").</summary>
    public string? Equipment { get; init; }
    public int? EstimatedWeeks { get; init; }
    public int? SessionsPerWeek { get; init; }

    /// <summary>Slug of a library exercise whose photo becomes the cover; must exist.</summary>
    public string? CoverExerciseSlug { get; init; }
    public List<ModuleSeedDto> Modules { get; init; } = [];
}

/// <summary>
/// DTO for seeding modules from JSON files.
/// </summary>
public sealed class ModuleSeedDto
{
    public required string Title { get; init; }
    public string? TitleEl { get; init; }
    public string? Description { get; init; }
    public string? DescriptionEl { get; init; }
    public int SortOrder { get; init; }
    public List<LessonSeedDto> Lessons { get; init; } = [];
}

/// <summary>
/// DTO for seeding lessons from JSON files.
/// </summary>
public sealed class LessonSeedDto
{
    public required string Title { get; init; }
    public string? TitleEl { get; init; }
    public string? Content { get; init; }
    public string? ContentEl { get; init; }
    public string? PdfPath { get; init; }
    public string? VideoPath { get; init; }
    public int? DurationMinutes { get; init; }
    public int SortOrder { get; init; }

    /// <summary>Exercises prescribed in this lesson, in display order.</summary>
    public List<LessonExerciseSeedDto> Exercises { get; init; } = [];
}

/// <summary>
/// DTO for seeding one exercise prescription inside a lesson; the slug must exist in the library.
/// </summary>
public sealed class LessonExerciseSeedDto
{
    public required string Slug { get; init; }
    public int? Sets { get; init; }

    /// <summary>Free text such as "8-12", "30 s" or "AMRAP"; shown as-is in both languages (no words).</summary>
    public string? Reps { get; init; }
    public int? RestSeconds { get; init; }
    public string? Notes { get; init; }
    public string? NotesEl { get; init; }
}
