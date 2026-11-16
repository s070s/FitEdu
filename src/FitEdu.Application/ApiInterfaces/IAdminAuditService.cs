using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Read-only view of the admin audit log (who disabled, re-roled, reset, impersonated or deleted whom, and
/// when). The rows are written by <see cref="IAdminUserService"/> and the sign-in service in the same save as
/// the action; nothing here, or anywhere else in the app, changes or removes one.
/// </summary>
public interface IAdminAuditService
{
    /// <summary>
    /// One page of the log, newest first. A <paramref name="memberId"/> limits it to the actions done to that
    /// member; their id stays in the log after the account is deleted.
    /// </summary>
    Task<PagedResult<AdminAuditEntryDto>> GetEntriesAsync(int page, int pageSize, string? memberId, CancellationToken cancellationToken = default);
}
