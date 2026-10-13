using FitEdu.Application.DTOs;
using FitEdu.Web.Extensions;
using System.Net.Http.Json;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the public testimonials API.
/// Turns the raw AvatarPath into a browser URL (see <see cref="MediaUrls"/>) so Blazor
/// components can use it directly as an img src.
/// </summary>
public class TestimonialConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), ITestimonialConsumer
{
    public async Task<IReadOnlyList<TestimonialDto>?> GetActiveAsync(string lang = "en", CancellationToken ct = default)
    {
        var response = await Http.GetAsync($"api/testimonials?lang={lang}", ct);
        if (!response.IsSuccessStatusCode)
            return null;

        var items = await response.Content.ReadFromJsonAsync<List<AdminTestimonialDto>>(ct);
        if (items is null)
            return null;

        return items.Select(t => new TestimonialDto(
            t.Id,
            t.FullName,
            t.RoleOrTitle,
            t.CompanyOrContext,
            t.QuoteText,
            MediaUrls.Resolve(t.AvatarPath),
            t.DisplayOrder
        )).ToList();
    }
}
