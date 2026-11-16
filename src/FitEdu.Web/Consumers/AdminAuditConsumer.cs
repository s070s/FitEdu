using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the admin audit log API.
/// </summary>
public class AdminAuditConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), IAdminAuditConsumer
{
    public Task<PagedResult<AdminAuditEntryDto>?> GetEntriesAsync(int page = 1, int pageSize = 20)
        => GetAsync<PagedResult<AdminAuditEntryDto>>($"api/admin/audit?page={page}&pageSize={pageSize}");
}
