using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for the admin audit log API (read-only).
/// </summary>
public interface IAdminAuditConsumer
{
    /// <summary>One page of the log, newest first; null when the request failed.</summary>
    Task<PagedResult<AdminAuditEntryDto>?> GetEntriesAsync(int page = 1, int pageSize = 20);
}
