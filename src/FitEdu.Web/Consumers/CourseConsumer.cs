using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using System.Net.Http.Json;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the course API.
/// </summary>
public class CourseConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), ICourseConsumer
{
    public async Task<PagedResult<CourseListItemDto>> GetCoursesAsync(
        int page = 1, int pageSize = 10, string lang = "en", Guid? categoryId = null, string? search = null,
        FitnessGoal? goal = null, CourseLevel? level = null, string? equipment = null)
    {
        // Enums travel by name (e.g. goal=FatLoss); the API model binder accepts names and numbers.
        var url = $"api/courses?page={page}&pageSize={pageSize}&lang={lang}"
            + OptionalQuery("categoryId", categoryId?.ToString())
            + OptionalQuery("search", search)
            + OptionalQuery("goal", goal?.ToString())
            + OptionalQuery("level", level?.ToString())
            + OptionalQuery("equipment", equipment);

        return await GetAsync<PagedResult<CourseListItemDto>>(url)
           ?? new PagedResult<CourseListItemDto>([], 0, page, pageSize);
    }

    public Task<CourseDetailDto?> GetCourseAsync(Guid courseId, string lang = "en")
        => GetAsync<CourseDetailDto>($"api/courses/{courseId}?lang={lang}");

    public async Task<EnrollmentResultDto?> EnrollAsync(Guid courseId)
    {
        await EnsureAuthorizationAsync();
        var response = await Http.PostAsync($"api/courses/{courseId}/enroll", null);
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return await response.Content.ReadFromJsonAsync<EnrollmentResultDto>();
        return null;
    }

    public Task<LessonDetailDto?> GetLessonAsync(Guid lessonId, string lang = "en")
        => GetAsync<LessonDetailDto>($"api/courses/lessons/{lessonId}?lang={lang}");

    public Task<LessonCompletionResultDto?> CompleteLessonAsync(Guid lessonId)
        => PostAsync<LessonCompletionResultDto>($"api/courses/lessons/{lessonId}/complete");

    public Task<CourseReviewsResponseDto?> GetReviewsAsync(Guid courseId)
        => GetAsync<CourseReviewsResponseDto>($"api/courses/{courseId}/reviews");

    public Task<MyCourseReviewDto?> SaveReviewAsync(Guid courseId, SaveCourseReviewRequest request)
        => PostJsonAsync<SaveCourseReviewRequest, MyCourseReviewDto>($"api/courses/{courseId}/reviews", request);
}
