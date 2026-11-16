using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for admin user management API operations.
/// </summary>
public interface IAdminUserConsumer
{
    Task<PagedResult<AdminUserDto>?> GetUsersAsync(int page = 1, int pageSize = 10, string? search = null, string sortBy = "email", string sortDir = "asc");
    Task<bool> DeleteUserAsync(string userId);
    Task<bool> SetPasswordAsync(string userId, string newPassword);
    Task<bool> DisableUserAsync(string userId);
    Task<bool> EnableUserAsync(string userId);
}
