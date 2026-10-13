using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Handles course discovery, enrollment, and lesson consumption for students.
/// </summary>
public interface ICourseService
{
    Task<PagedResult<CourseListItemDto>> GetPublishedCoursesAsync(
        string userId, int page, int pageSize, string lang, Guid? categoryId = null, string? search = null,
        FitnessGoal? goal = null, CourseLevel? level = null, string? equipment = null,
        CancellationToken cancellationToken = default);
    /// <summary>Every published course id, for the sitemap.</summary>
    Task<IReadOnlyList<Guid>> GetPublishedCourseIdsAsync(CancellationToken cancellationToken = default);
    Task<CourseDetailDto?> GetCourseDetailAsync(string userId, Guid courseId, string lang, CancellationToken cancellationToken = default);
    /// <summary>
    /// Every programme the member has joined, published or not, finished or not: the one count the
    /// plan limit is checked against.
    /// </summary>
    Task<int> CountEnrollmentsAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<EnrollmentResultDto>> EnrollAsync(string userId, Guid courseId, CancellationToken cancellationToken = default);
    Task<ServiceResult<LessonDetailDto>> GetLessonDetailAsync(string userId, Guid lessonId, string lang, CancellationToken cancellationToken = default);
    Task<ServiceResult<LessonCompletionResultDto>> CompleteLessonAsync(string userId, Guid lessonId, CancellationToken cancellationToken = default);
}
