using Microsoft.AspNetCore.Components.Forms;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the admin blog management API.
/// </summary>
public class AdminBlogConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), IAdminBlogConsumer
{
    public Task<PagedResult<AdminBlogArticleDto>?> GetArticlesAsync(
        int page = 1, int pageSize = 10, string? search = null, string sortBy = "createdat", string sortDir = "desc", CancellationToken ct = default)
    {
        var url = $"api/admin/blog?page={page}&pageSize={pageSize}&sortBy={sortBy}&sortDir={sortDir}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        return GetAsync<PagedResult<AdminBlogArticleDto>>(url, ct);
    }

    public Task<AdminBlogArticleDto?> GetArticleAsync(Guid id, CancellationToken ct = default)
        => GetAsync<AdminBlogArticleDto>($"api/admin/blog/{id}", ct);

    public Task<AdminBlogArticleDto?> CreateArticleAsync(
        SaveBlogArticleRequest request, CancellationToken ct = default)
        => PostJsonAsync<SaveBlogArticleRequest, AdminBlogArticleDto>("api/admin/blog", request, ct);

    public Task<ApiActionResult> UpdateArticleAsync(
        Guid id, SaveBlogArticleRequest request, CancellationToken ct = default)
        => PutJsonResultAsync($"api/admin/blog/{id}", request, ct);

    public Task<bool> PublishArticleAsync(Guid id, CancellationToken ct = default)
        => ActionAsync($"api/admin/blog/{id}/publish", ct);

    public Task<bool> UnpublishArticleAsync(Guid id, CancellationToken ct = default)
        => ActionAsync($"api/admin/blog/{id}/unpublish", ct);

    public Task<bool> DeleteArticleAsync(Guid id, CancellationToken ct = default)
        => DeleteAsync($"api/admin/blog/{id}", ct);

    public async Task<string?> UploadCoverImageAsync(Guid id, IBrowserFile file, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var stream = file.OpenReadStream(UploadLimits.MaxCoverImageBytes, ct);
        var streamContent = new StreamContent(stream);
        if (!string.IsNullOrEmpty(file.ContentType))
            streamContent.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
        content.Add(streamContent, "file", file.Name);

        var result = await PostFormAsync<CoverUploadResult>($"api/admin/blog/{id}/upload/cover", content, ct);
        return result?.CoverImageUrl;
    }

    private sealed record CoverUploadResult(string CoverImageUrl);
}
