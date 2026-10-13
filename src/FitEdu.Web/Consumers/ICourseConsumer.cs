using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for course-related API operations.
/// </summary>
public interface ICourseConsumer
{
    Task<PagedResult<CourseListItemDto>> GetCoursesAsync(
        int page = 1, int pageSize = 10, string lang = "en", Guid? categoryId = null, string? search = null,
        FitnessGoal? goal = null, CourseLevel? level = null, string? equipment = null);
    Task<CourseDetailDto?> GetCourseAsync(Guid courseId, string lang = "en");
    /// <summary>The same programme read, telling a 404 apart from a failed request (for the course page).</summary>
    Task<ApiLookup<CourseDetailDto>> LookupCourseAsync(Guid courseId, string lang = "en");
    /// <summary>How many programmes the member has joined; null when the API could not say.</summary>
    Task<int?> CountEnrollmentsAsync();
    Task<EnrollmentResultDto?> EnrollAsync(Guid courseId);
    Task<LessonDetailDto?> GetLessonAsync(Guid lessonId, string lang = "en");
    Task<LessonCompletionResultDto?> CompleteLessonAsync(Guid lessonId);
    Task<CourseReviewsResponseDto?> GetReviewsAsync(Guid courseId);
    Task<MyCourseReviewDto?> SaveReviewAsync(Guid courseId, SaveCourseReviewRequest request);
}
