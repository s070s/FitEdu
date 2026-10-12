namespace FitEdu.Application.DTOs;

/// <summary>
/// DTO for seeding one landing-page testimonial from JSON (src/FitEdu.Shared/JSON/Showcase/testimonials.json).
/// There is no Greek company/context column, so the context lives in RoleOrTitle and RoleOrTitleEl.
/// </summary>
public sealed class TestimonialSeedDto
{
    public required string FullName { get; init; }
    public string? RoleOrTitle { get; init; }
    public string? RoleOrTitleEl { get; init; }
    public required string QuoteText { get; init; }
    public string? QuoteTextEl { get; init; }
    public int DisplayOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>
/// DTO for seeding one scheduled live class from JSON (src/FitEdu.Shared/JSON/Showcase/live-classes.json).
/// </summary>
public sealed class LiveClassSeedDto
{
    public required string Title { get; init; }
    public string? TitleEl { get; init; }

    /// <summary>English title of the related course; null for a class tied to no course.</summary>
    public string? CourseTitle { get; init; }

    /// <summary>The class starts on the N-th Monday-to-Friday day after the seed date (the seed date itself does not count).</summary>
    public int WeekdayOffset { get; init; }

    /// <summary>Start hour in UTC (0-23).</summary>
    public int HourUtc { get; init; }

    public int DurationMinutes { get; init; }
    public int MaxParticipants { get; init; }
}
