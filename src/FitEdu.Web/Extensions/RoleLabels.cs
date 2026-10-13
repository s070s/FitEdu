using FitEdu.Shared.Resources;
using FitEdu.Web.Layout;

namespace FitEdu.Web.Extensions;

/// <summary>Localized labels for the identity roles the admin user list shows.</summary>
public static class RoleLabels
{
    /// <summary>The label of one stored role name; an unknown role is shown as it is.</summary>
    public static string Label(string roleName) => roleName switch
    {
        NavItems.AdminRole => AdminRes.RoleAdmin,
        NavItems.StudentRole => AdminRes.RoleStudent,
        _ => roleName
    };
}
