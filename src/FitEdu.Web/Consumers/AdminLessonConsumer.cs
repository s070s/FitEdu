using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the admin lesson management API.
/// </summary>
public class AdminLessonConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider, IHttpClientFactory httpClientFactory)
    : ApiClientBase(http, tokenProvider), IAdminLessonConsumer
{
    /// <summary>The named client for file uploads (see ServiceRegistrationExtensions): no client timeout.</summary>
    public const string UploadClientName = "LessonUploads";

    /// <summary>The longest one upload may take: a 500 MB video over a slow connection.</summary>
    internal static readonly TimeSpan DefaultUploadTimeout = TimeSpan.FromMinutes(30);

    /// <summary>The time one upload call may take before it gives up; a test shortens it.</summary>
    internal TimeSpan UploadTimeout { get; init; } = DefaultUploadTimeout;

    public async Task<List<AdminLessonDto>> GetLessonsByModuleAsync(Guid moduleId)
        => await GetAsync<List<AdminLessonDto>>($"api/admin/lessons/module/{moduleId}") ?? [];

    public Task<AdminLessonDto?> CreateLessonAsync(SaveLessonRequest request)
        => PostJsonAsync<SaveLessonRequest, AdminLessonDto>("api/admin/lessons", request);

    public Task<AdminLessonDto?> UpdateLessonAsync(Guid id, SaveLessonRequest request)
        => PutJsonAsync<SaveLessonRequest, AdminLessonDto>($"api/admin/lessons/{id}", request);

    public Task<bool> DeleteLessonAsync(Guid id)
        => DeleteAsync($"api/admin/lessons/{id}");

    public async Task<string?> UploadPdfAsync(Guid id, IBrowserFile file)
    {
        var result = await UploadAsync<UploadPdfResponse>($"api/admin/lessons/{id}/upload/pdf", file, UploadLimits.MaxPdfBytes);
        return result?.PdfPath;
    }

    public async Task<string?> UploadVideoAsync(Guid id, IBrowserFile file)
    {
        var result = await UploadAsync<UploadVideoResponse>($"api/admin/lessons/{id}/upload/video", file, UploadLimits.MaxVideoBytes);
        return result?.VideoPath;
    }

    /// <summary>
    /// Sends the file through the upload client, which has no client timeout: a 500 MB video can take
    /// minutes, and the 30-second consumer timeout would cut it off after the old video was already
    /// replaced. <see cref="UploadTimeout"/> bounds the call instead. Any failure to read the file or
    /// reach the API gives null, so the page shows its "upload failed" message rather than an exception.
    /// </summary>
    private async Task<TResult?> UploadAsync<TResult>(string url, IBrowserFile file, long maxBytes)
    {
        using var timeout = new CancellationTokenSource(UploadTimeout);
        try
        {
            using var content = new MultipartFormDataContent();
            using var stream = file.OpenReadStream(maxBytes, timeout.Token);
            var streamContent = new StreamContent(stream);
            if (!string.IsNullOrEmpty(file.ContentType))
                streamContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(file.ContentType);
            content.Add(streamContent, "file", file.Name);

            // The upload client is not this consumer's own client, so the bearer token is added to the request.
            await EnsureAuthorizationAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Authorization = Http.DefaultRequestHeaders.Authorization;

            using var response = await httpClientFactory.CreateClient(UploadClientName).SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return default;

            return await response.Content.ReadFromJsonAsync<TResult>(cancellationToken: timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return default;
        }
    }

    private sealed record UploadPdfResponse(string PdfPath);
    private sealed record UploadVideoResponse(string VideoPath);
}
