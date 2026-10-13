using FitEdu.Domain.Enums;

namespace FitEdu.Application.DTOs;

/// <summary>
/// Full course detail including modules and progress. The fitness fields match
/// <see cref="CourseListItemDto"/>.
/// </summary>
public record CourseDetailDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsEnrolled,
    bool IsCompleted,
    int CompletedLessons,
    int TotalLessons,
    double ProgressPercent,
    List<ModuleDto> Modules,
    SubscriptionTier RequiredTier,
    CourseLevel Level = CourseLevel.Beginner,
    FitnessGoal Goal = FitnessGoal.GeneralFitness,
    string? Equipment = null,
    int? EstimatedWeeks = null,
    int? SessionsPerWeek = null,
    string? CoverImageUrl = null,
    string? CategoryName = null
);

/// <summary>
/// Module within a course.
/// </summary>
public record ModuleDto(
    Guid Id,
    string Title,
    string? Description,
    int SortOrder,
    List<LessonSummaryDto> Lessons
);

/// <summary>
/// Lesson summary for module listing. ExerciseCount counts the published exercises the lesson
/// prescribes, the same ones the lesson page shows.
/// </summary>
public record LessonSummaryDto(
    Guid Id,
    string Title,
    int ContentType,
    int? DurationMinutes,
    int SortOrder,
    bool IsCompleted,
    int ExerciseCount = 0
);
