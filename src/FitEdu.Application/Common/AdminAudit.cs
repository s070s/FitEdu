using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Data;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;

namespace FitEdu.Application.Common;

/// <summary>
/// Stages the audit row of an admin action in the change tracker, so the save that stores the action stores
/// the row too (one transaction: no action without its row, no row without its action). Staging does not save;
/// when the action fails the row is dropped again with <see cref="Discard"/>, so a later save of the same
/// request cannot store a row for something that did not happen.
/// </summary>
public static class AdminAudit
{
    public static AdminAuditEntry Stage(
        IApplicationDbContext context,
        string actorUserId,
        string targetUserId,
        AdminAuditAction action,
        DateTimeOffset occurredAt,
        string? detail = null)
    {
        var entry = new AdminAuditEntry
        {
            ActorUserId = actorUserId,
            TargetUserId = targetUserId,
            Action = action,
            Detail = detail,
            OccurredAt = occurredAt
        };
        context.AdminAuditEntries.Add(entry);
        return entry;
    }

    public static void Discard(IApplicationDbContext context, AdminAuditEntry entry)
        => context.Entry(entry).State = EntityState.Detached;
}
