using FitEdu.Domain.Enums;

namespace FitEdu.Domain.Entities;

/// <summary>
/// One admin action on a member account (who did what to whom, and when). The log only grows: the app has
/// no way to edit or delete a row. Both user ids are plain columns, not foreign keys, so a row outlives the
/// member it names and the deletion of an account is never blocked or cascaded by it. A row holds ids, the
/// action and a short detail such as a role name; never a password, a token or free text about a person.
/// </summary>
public class AdminAuditEntry
{
    /// <summary>The longest <see cref="Detail"/>; an Identity role name is at most this long.</summary>
    public const int DetailMaxLength = 256;

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The admin who acted.</summary>
    public required string ActorUserId { get; set; }

    /// <summary>The member the action was done to; for a deleted account the id is all that is left of it.</summary>
    public required string TargetUserId { get; set; }

    public AdminAuditAction Action { get; set; }

    /// <summary>The role name for a role change; null for the other actions.</summary>
    public string? Detail { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
