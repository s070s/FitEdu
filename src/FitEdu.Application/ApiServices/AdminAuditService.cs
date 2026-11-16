using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Data;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Identity;

namespace FitEdu.Application.ApiServices;

/// <summary>
/// Lists the admin audit log, newest first.
/// </summary>
public class AdminAuditService(IApplicationDbContext context, UserManager<ApplicationUser> userManager) : IAdminAuditService
{
    public async Task<PagedResult<AdminAuditEntryDto>> GetEntriesAsync(
        int page, int pageSize, string? memberId, CancellationToken cancellationToken = default)
    {
        var query = context.AdminAuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(memberId))
        {
            var filteredMemberId = memberId.Trim();
            query = query.Where(entry => entry.TargetUserId == filteredMemberId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // The id breaks a tie between two actions in the same instant, so a page boundary never repeats or skips a row.
        var entries = await query
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Single query for the accounts the page names; an id with no account (a deleted member) has no email.
        var accountIds = entries
            .SelectMany(entry => new[] { entry.ActorUserId, entry.TargetUserId })
            .Distinct()
            .ToList();
        var emailByAccountId = await userManager.Users
            .AsNoTracking()
            .Where(user => accountIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Email })
            .ToDictionaryAsync(user => user.Id, user => user.Email, cancellationToken);

        var items = entries
            .Select(entry => new AdminAuditEntryDto(
                entry.Id,
                entry.ActorUserId,
                emailByAccountId.GetValueOrDefault(entry.ActorUserId),
                entry.TargetUserId,
                emailByAccountId.GetValueOrDefault(entry.TargetUserId),
                entry.Action,
                entry.Detail,
                entry.OccurredAt))
            .ToList();

        return new PagedResult<AdminAuditEntryDto>(items, totalCount, page, pageSize);
    }
}
