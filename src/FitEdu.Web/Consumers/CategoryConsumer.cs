using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the public category API.
/// </summary>
public class CategoryConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), ICategoryConsumer
{
    public async Task<List<CategoryDto>> GetCategoriesAsync(string scope, string lang = "en")
        => await GetAsync<List<CategoryDto>>($"api/categories?scope={Uri.EscapeDataString(scope)}&lang={lang}") ?? [];
}
