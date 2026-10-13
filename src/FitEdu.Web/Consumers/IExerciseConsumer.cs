using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for the public exercise library API. Failures degrade to empty results;
/// <see cref="GetBySlugAsync"/> also reports whether a missing exercise was a 404 or a failed request.
/// </summary>
public interface IExerciseConsumer
{
    Task<PagedResult<ExerciseListItemDto>> ListAsync(
        int page = 1, int pageSize = ExerciseLibraryPaging.DefaultPageSize, string lang = "en", ExerciseFilterDto? filter = null);
    Task<ApiLookup<ExerciseDetailDto>> GetBySlugAsync(string slug, string lang = "en");
    Task<ExerciseFacetsDto> GetFacetsAsync();
    Task<IReadOnlyList<LessonUsingExerciseDto>> GetLessonsUsingExerciseAsync(string slug, string lang = "en");
}
