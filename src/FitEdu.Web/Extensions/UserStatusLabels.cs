using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;

namespace FitEdu.Web.Extensions;

/// <summary>
/// Localized labels for the user-facing subset of <see cref="UserStatus"/> (the training level).
/// <see cref="UserStatus.Unknown"/> is a system default, never a real user choice, so it is
/// intentionally excluded from <see cref="Selectable"/>.
/// </summary>
public static class UserStatusLabels
{
    public static readonly UserStatus[] Selectable =
    [
        UserStatus.Beginner,
        UserStatus.Intermediate,
        UserStatus.Advanced,
        UserStatus.Other
    ];

    public static string Label(UserStatus status) => status switch
    {
        UserStatus.Beginner => GlobalRes.StatusBeginner,
        UserStatus.Intermediate => GlobalRes.StatusIntermediate,
        UserStatus.Advanced => GlobalRes.StatusAdvanced,
        UserStatus.Other => GlobalRes.StatusOther,
        _ => status.ToString()
    };

    /// <summary>Label for a level sent as its enum name (the admin user list); unknown names are shown as they are.</summary>
    public static string Label(string statusName) =>
        Enum.TryParse<UserStatus>(statusName, out var status) ? Label(status) : statusName;
}
