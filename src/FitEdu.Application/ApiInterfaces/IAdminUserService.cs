using FitEdu.Application.Common;
using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Admin-only user and role management: listing/search, role assignment, enable/disable,
/// password resets, and impersonation.
/// </summary>
public interface IAdminUserService
{
    Task<PagedResult<AdminUserDto>> GetUsersAsync(int page, int pageSize, string? search, string sortBy, string sortDir, CancellationToken cancellationToken = default);
    Task<AdminUserDetailDto?> GetUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<string>> AssignRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
    Task<ServiceResult<string>> RemoveRoleAsync(string userId, string roleName, CancellationToken cancellationToken = default);
    Task<List<string?>> GetRolesAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<string>> CreateRoleAsync(string roleName, CancellationToken cancellationToken = default);
    Task<ServiceResult<string>> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<AdminUserSearchResultDto>> SearchUsersAsync(string query, CancellationToken cancellationToken = default);

    /// <param name="userId">The user to reset; the emailed link is built from <c>App:BaseUrl</c> like every account email.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ServiceResult<bool>> ForcePasswordResetAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> DisableUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> EnableUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<ServiceResult<AuthResponseDto>> ImpersonateUserAsync(string userId, string adminId, CancellationToken cancellationToken = default);
    Task<ServiceResult<bool>> SetPasswordAsync(string userId, string newPassword, CancellationToken cancellationToken = default);
}
