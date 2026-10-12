using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Admin CRUD operations for courses, plus lesson create/update (which carry the lesson's exercises).
/// Saves that name an exercise slug missing from the library return 400 (BadRequest).
/// </summary>
public interface IAdminCourseService
{
    Task<AdminCourseDto?> GetCourseByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<PagedResult<AdminCourseDto>> GetCoursesAsync(int page, int pageSize, string sortBy, string sortDir, string? search = null, CancellationToken cancellationToken = default);
    Task<ServiceResult<AdminCourseDto>> CreateCourseAsync(SaveCourseRequest request, string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<AdminCourseDto>> UpdateCourseAsync(Guid id, SaveCourseRequest request, string userId, CancellationToken cancellationToken = default);
    Task<bool> DeleteCourseAsync(Guid id, string userId, CancellationToken cancellationToken = default);
    Task<bool> PublishCourseAsync(Guid id, string userId, CancellationToken cancellationToken = default);
    Task<bool> UnpublishCourseAsync(Guid id, string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<AdminLessonDto>> CreateLessonAsync(SaveLessonRequest request, string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<AdminLessonDto>> UpdateLessonAsync(Guid id, SaveLessonRequest request, string userId, CancellationToken cancellationToken = default);
}
