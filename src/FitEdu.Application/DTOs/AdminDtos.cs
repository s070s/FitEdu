using System.ComponentModel.DataAnnotations;
using FitEdu.Domain.Enums;

namespace FitEdu.Application.DTOs;

/// <summary>
/// Analytics summary for admin dashboard.
/// </summary>
public record AnalyticsSummaryDto(
    int TotalUsers,
    int ActiveUsers,
    int TotalEnrollments,
    int CompletedCourses,
    List<CourseStatDto> CourseStats,
    List<AssessmentStatDto> AssessmentStats
);

/// <summary>
/// Per-course enrollment and completion stats.
/// </summary>
public record CourseStatDto(
    string CourseTitle,
    int EnrollmentCount,
    int CompletionCount
);

/// <summary>
/// Per-assessment submission stats.
/// </summary>
public record AssessmentStatDto(
    string AssessmentTitle,
    int SubmissionCount
);

/// <summary>
/// Admin course DTO with full details.
/// </summary>
public record AdminCourseDto(
    Guid Id,
    string TitleEn,
    string? TitleEl,
    string? DescriptionEn,
    string? DescriptionEl,
    bool IsPublished,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    int ModuleCount,
    int TotalLessons,
    int EnrollmentCount,
    SubscriptionTier RequiredTier,
    Guid? CategoryId = null,
    string? CategoryNameEn = null,
    CourseLevel Level = CourseLevel.Beginner,
    FitnessGoal Goal = FitnessGoal.GeneralFitness,
    string? Equipment = null,
    int? EstimatedWeeks = null,
    int? SessionsPerWeek = null,
    string? CoverExerciseSlug = null
);

/// <summary>
/// Request to create/update a course. When <paramref name="NewCategoryName"/> is non-blank it wins
/// over <paramref name="CategoryId"/> and a category is get-or-created by case-insensitive name match.
/// <paramref name="Equipment"/> is a comma-separated list of codes (e.g. "dumbbells,mat").
/// <paramref name="CoverExerciseSlug"/> must name an exercise of the library; blank clears the cover.
/// </summary>
public record SaveCourseRequest(
    [Required, MaxLength(200)] string TitleEn,
    [MaxLength(200)] string? TitleEl,
    [MaxLength(1000)] string? DescriptionEn,
    [MaxLength(1000)] string? DescriptionEl,
    SubscriptionTier RequiredTier,
    Guid? CategoryId = null,
    [MaxLength(100)] string? NewCategoryName = null,
    [EnumDataType(typeof(CourseLevel))] CourseLevel Level = CourseLevel.Beginner,
    [EnumDataType(typeof(FitnessGoal))] FitnessGoal Goal = FitnessGoal.GeneralFitness,
    [MaxLength(120)] string? Equipment = null,
    [Range(1, 104)] int? EstimatedWeeks = null,
    [Range(1, 14)] int? SessionsPerWeek = null,
    [MaxLength(120)] string? CoverExerciseSlug = null
);

/// <summary>
/// Admin module DTO.
/// </summary>
public record AdminModuleDto(
    Guid Id,
    string TitleEn,
    string? TitleEl,
    string? DescriptionEn,
    string? DescriptionEl,
    int SortOrder,
    Guid CourseId,
    int LessonCount
);

/// <summary>
/// Request to create/update a module.
/// </summary>
public record SaveModuleRequest(
    [Required, MaxLength(200)] string TitleEn,
    [MaxLength(200)] string? TitleEl,
    [MaxLength(1000)] string? DescriptionEn,
    [MaxLength(1000)] string? DescriptionEl,
    int SortOrder,
    Guid CourseId
);

/// <summary>
/// Admin lesson DTO.
/// </summary>
public record AdminLessonDto(
    Guid Id,
    string TitleEn,
    string? TitleEl,
    string? ContentEn,
    string? ContentEl,
    string? PdfPath,
    string? VideoPath,
    int? DurationMinutes,
    int SortOrder,
    Guid ModuleId,
    bool IsPublished,
    List<LessonExerciseEditDto>? Exercises = null
);

/// <summary>
/// Request to create/update a lesson. <paramref name="Exercises"/> null keeps the lesson's current
/// exercises; a list (even an empty one) replaces them wholesale, in list order.
/// </summary>
public record SaveLessonRequest(
    [Required, MaxLength(200)] string TitleEn,
    [MaxLength(200)] string? TitleEl,
    string? ContentEn,
    string? ContentEl,
    [MaxLength(500)] string? VideoUrl,
    int? DurationMinutes,
    int SortOrder,
    Guid ModuleId,
    [MaxLength(50)] List<LessonExerciseEditDto>? Exercises = null
);

/// <summary>
/// One exercise row of the admin lesson editor. Saves identify the exercise by
/// <paramref name="Slug"/>; <paramref name="Name"/> (English) is filled on reads for display and
/// ignored on saves. <paramref name="Reps"/> is free text ("8–12", "30 s", "AMRAP").
/// </summary>
public record LessonExerciseEditDto(
    [Required, MaxLength(120)] string Slug,
    [Range(1, 100)] int? Sets = null,
    [MaxLength(40)] string? Reps = null,
    [Range(0, 3600)] int? RestSeconds = null,
    [MaxLength(500)] string? NotesEn = null,
    [MaxLength(500)] string? NotesEl = null,
    string? Name = null
);

/// <summary>
/// User management DTO for admin.
/// </summary>
public record AdminUserDto(
    string Id,
    string Email,
    string FirstName,
    string LastName,
    string? DisplayName,
    bool EmailConfirmed,
    bool IsEnabled,
    string Status,
    List<string> Roles,
    string SubscriptionTier,
    DateTime CreatedAt,
    DateTime? LastSeenAt = null
);

/// <summary>
/// Request to force password reset.
/// </summary>
public record ForcePasswordResetRequest(
    string UserId
);
