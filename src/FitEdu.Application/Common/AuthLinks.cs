namespace FitEdu.Application.Common;

/// <summary>
/// The one place that builds the links in the account emails (confirm email, reset password), so the
/// address and the escaping are the same on every path that sends them.
/// </summary>
public static class AuthLinks
{
    public const string ConfirmEmailPath = "/confirm-email";

    public const string ResetPasswordPath = "/reset-password";

    public static string ConfirmEmail(string baseUrl, string userId, string token) =>
        $"{baseUrl.TrimEnd('/')}{ConfirmEmailPath}?userId={Uri.EscapeDataString(userId)}&token={Uri.EscapeDataString(token)}";

    public static string ResetPassword(string baseUrl, string email, string token) =>
        $"{baseUrl.TrimEnd('/')}{ResetPasswordPath}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
}
