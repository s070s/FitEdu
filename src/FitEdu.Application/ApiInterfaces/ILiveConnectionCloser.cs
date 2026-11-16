namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Closes the open real-time (SignalR) connections of a member. An admin action that ends the member's
/// sessions (disable, role change, password reset) calls it, so a chat, call or notification connection that
/// is already open stops with the session instead of living on until the client reconnects. Defined here
/// (framework-agnostic) so the Application layer does not depend on SignalR; the Web layer implements it.
/// </summary>
public interface ILiveConnectionCloser
{
    void CloseAll(string userId);
}
