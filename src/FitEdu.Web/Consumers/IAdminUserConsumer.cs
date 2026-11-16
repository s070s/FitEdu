using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for admin user management API operations.
/// </summary>
public interface IAdminUserConsumer
{
    Task<PagedResult<AdminUserDto>?> GetUsersAsync(int page = 1, int pageSize = 10, string? search = null, string sortBy = "email", string sortDir = "asc");
    Task<bool> DeleteUserAsync(string userId);
    /// <summary>Sets the member's password; a refusal carries the server's rule message in its Detail.</summary>
    Task<ApiActionResult> SetPasswordAsync(string userId, string newPassword);
    Task<bool> DisableUserAsync(string userId);
    Task<bool> EnableUserAsync(string userId);
}
