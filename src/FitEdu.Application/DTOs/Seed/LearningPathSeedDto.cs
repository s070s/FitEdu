namespace FitEdu.Application.DTOs;

/// <summary>
/// DTO for seeding one learning path from JSON (src/FitEdu.Shared/JSON/Paths).
/// </summary>
public sealed class LearningPathSeedDto
{
    public required string Title { get; init; }
    public string? TitleEl { get; init; }
    public string? Description { get; init; }
    public string? DescriptionEl { get; init; }

    /// <summary>Category name (get-or-create by English name, shared with the courses).</summary>
    public string? Category { get; init; }

    /// <summary>Greek category name; fills Category.NameEl when the category has none yet.</summary>
    public string? CategoryEl { get; init; }

    public int DisplayOrder { get; init; }

    /// <summary>The public /paths list shows only published paths, so a seeded path is published unless the file says otherwise.</summary>
    public bool IsPublished { get; init; } = true;

    /// <summary>English titles of the courses, in step order; each must match a seeded course.</summary>
    public List<string> CourseTitles { get; init; } = [];
}
