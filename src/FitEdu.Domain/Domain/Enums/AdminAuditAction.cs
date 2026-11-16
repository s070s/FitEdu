namespace FitEdu.Domain.Enums;

/// <summary>
/// What an admin did to a member account, as recorded in the admin audit log
/// (<see cref="Entities.AdminAuditEntry"/>). Stored as int, so a value is never renumbered or reused.
/// </summary>
public enum AdminAuditAction
{
    UserDisabled = 1,
    UserEnabled = 2,
    RoleAdded = 3,
    RoleRemoved = 4,
    PasswordSet = 5,
    ImpersonationStarted = 6,
    AccountDeleted = 7
}
