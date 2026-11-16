using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Admin-only, read-only view of the admin audit log. There is deliberately no way to add, change or remove a
/// row here: the rows are written by the admin actions themselves.
/// </summary>
[ApiController]
[Route("api/admin/audit")]
[Authorize(Policy = "AdminOnly")]
[Tags("Admin · Audit")]
[Produces("application/json", "application/problem+json")]
public class AdminAuditController(IAdminAuditService adminAuditService) : ControllerBase
{
    /// <summary>
    /// List what admins did to member accounts (disable, enable, role added or removed, password set,
    /// impersonation started, account deleted), newest first. Rows hold ids, the action, the time and the acting
    /// admin; the emails are those of the accounts that still exist. <c>memberId</c> limits the list to the actions
    /// done to that member; it also finds the rows of a deleted member.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<AdminAuditEntryDto>>> GetEntries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? memberId = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = PagingParams.Normalize(page, pageSize);

        var result = await adminAuditService.GetEntriesAsync(page, pageSize, memberId, cancellationToken);
        return Ok(result);
    }
}
