using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// HTTP consumer for the admin user management API.
/// </summary>
public class AdminUserConsumer(HttpClient http, FitEdu.Web.Services.ApiTokenProvider tokenProvider) : ApiClientBase(http, tokenProvider), IAdminUserConsumer
{
    public Task<PagedResult<AdminUserDto>?> GetUsersAsync(
        int page = 1, int pageSize = 10, string? search = null, string sortBy = "email", string sortDir = "asc")
    {
        var url = $"api/admin/users?page={page}&pageSize={pageSize}" +
                  $"&sortBy={Uri.EscapeDataString(sortBy)}" +
                  $"&sortDir={Uri.EscapeDataString(sortDir)}";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search)}";
        return GetAsync<PagedResult<AdminUserDto>>(url);
    }

    public Task<bool> DeleteUserAsync(string userId)
        => DeleteAsync($"api/admin/users/{userId}");

    public Task<bool> DisableUserAsync(string userId)
        => ActionAsync($"api/admin/users/{userId}/disable");

    public Task<bool> SetPasswordAsync(string userId, string newPassword)
        => PostJsonActionAsync(
               $"api/admin/users/{userId}/set-password",
               new AdminSetPasswordDto { NewPassword = newPassword });

    public Task<bool> EnableUserAsync(string userId)
        => ActionAsync($"api/admin/users/{userId}/enable");
}
