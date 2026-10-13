using FitEdu.Domain.Enums;

namespace FitEdu.Application.DTOs;

/// <summary>
/// Course summary for catalog listing. Equipment holds comma-joined codes; CoverImageUrl is the
/// cover exercise's start photo, or null when the course has no cover exercise. IsCompleted is true
/// when the member finished every lesson of a programme they joined.
/// </summary>
public record CourseListItemDto(
    Guid Id,
    string Title,
    string? Description,
    bool IsEnrolled,
    int TotalLessons,
    SubscriptionTier RequiredTier = SubscriptionTier.Free,
    Guid? CategoryId = null,
    string? CategoryName = null,
    double? AverageRating = null,
    int ReviewCount = 0,
    CourseLevel Level = CourseLevel.Beginner,
    FitnessGoal Goal = FitnessGoal.GeneralFitness,
    string? Equipment = null,
    int? EstimatedWeeks = null,
    int? SessionsPerWeek = null,
    string? CoverImageUrl = null,
    bool IsCompleted = false
);
