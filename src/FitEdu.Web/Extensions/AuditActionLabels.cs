using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>Localized labels for what an admin did to an account, as the audit log page shows it.</summary>
public static class AuditActionLabels
{
    public static string Label(AdminAuditAction action) => action switch
    {
        AdminAuditAction.UserDisabled => AdminRes.AuditActionUserDisabled,
        AdminAuditAction.UserEnabled => AdminRes.AuditActionUserEnabled,
        AdminAuditAction.RoleAdded => AdminRes.AuditActionRoleAdded,
        AdminAuditAction.RoleRemoved => AdminRes.AuditActionRoleRemoved,
        AdminAuditAction.PasswordSet => AdminRes.AuditActionPasswordSet,
        AdminAuditAction.ImpersonationStarted => AdminRes.AuditActionImpersonationStarted,
        AdminAuditAction.AccountDeleted => AdminRes.AuditActionAccountDeleted,
        _ => action.ToString()
    };
}
