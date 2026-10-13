using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the public exercise library API.
/// </summary>
public class ExerciseConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), IExerciseConsumer
{
    public async Task<PagedResult<ExerciseListItemDto>> ListAsync(
        int page = 1, int pageSize = ExerciseLibraryPaging.DefaultPageSize, string lang = "en", ExerciseFilterDto? filter = null)
    {
        var url = $"api/exercises?page={page}&pageSize={pageSize}&lang={lang}"
            + OptionalQuery("search", filter?.Search)
            + OptionalQuery("muscle", filter?.Muscle)
            + OptionalQuery("equipment", filter?.Equipment)
            + OptionalQuery("level", filter?.Level)
            + OptionalQuery("category", filter?.Category);

        return await GetAsync<PagedResult<ExerciseListItemDto>>(url)
           ?? new PagedResult<ExerciseListItemDto>([], 0, page, pageSize);
    }

    public Task<ApiLookup<ExerciseDetailDto>> GetBySlugAsync(string slug, string lang = "en")
        => GetLookupAsync<ExerciseDetailDto>($"api/exercises/{Uri.EscapeDataString(slug)}?lang={lang}");

    public async Task<ExerciseFacetsDto> GetFacetsAsync()
        => await GetAsync<ExerciseFacetsDto>("api/exercises/facets") ?? ExerciseFacetsDto.Empty;

    public async Task<IReadOnlyList<LessonUsingExerciseDto>> GetLessonsUsingExerciseAsync(string slug, string lang = "en")
        => await GetAsync<List<LessonUsingExerciseDto>>($"api/exercises/{Uri.EscapeDataString(slug)}/lessons?lang={lang}")
           ?? [];
}
