using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Callable-user lookups. Video calls are available to every authenticated, enabled user.
/// </summary>
public interface ICallQueryService
{
    Task<List<ChatUserDto>> GetCallableUsersAsync(string callerId, string? search);
}
