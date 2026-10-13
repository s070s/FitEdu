using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Read access to the public exercise library (published exercises only).
/// </summary>
public interface IExerciseService
{
    Task<PagedResult<ExerciseListItemDto>> GetPublishedExercisesAsync(
        int page, int pageSize, string lang, ExerciseFilterDto? filter = null,
        CancellationToken cancellationToken = default);
    Task<ExerciseDetailDto?> GetPublishedBySlugAsync(string slug, string lang, CancellationToken cancellationToken = default);
    Task<ExerciseFacetsDto> GetFacetsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LessonUsingExerciseDto>> GetLessonsUsingExerciseAsync(
        string slug, string lang, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetPublishedSlugsAsync(CancellationToken cancellationToken = default);
}
