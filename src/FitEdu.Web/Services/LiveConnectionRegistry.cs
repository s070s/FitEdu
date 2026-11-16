using FitEdu.Application.ApiInterfaces;
using Microsoft.AspNetCore.SignalR;

namespace FitEdu.Web.Services;

/// <summary>
/// Knows every open chat, call and notification hub connection per user, so an admin action that ends a
/// member's sessions can close them on purpose (<see cref="ILiveConnectionCloser"/>). The hubs register a
/// connection once their own connect work has succeeded and unregister it when it closes. Singleton: it
/// outlives each hub instance, which SignalR creates per call.
/// </summary>
public sealed class LiveConnectionRegistry : ILiveConnectionCloser
{
    // userId -> (connectionId -> context), guarded by one lock so a user's entry can be removed when their
    // last connection closes without racing a parallel Register (no empty entry is kept per past user).
    private readonly Dictionary<string, Dictionary<string, HubCallerContext>> _connectionsByUser = new();
    private readonly Lock _gate = new();

    public void Register(HubCallerContext context)
    {
        var userId = context.UserIdentifier;
        if (userId is null)
            return;

        lock (_gate)
        {
            if (!_connectionsByUser.TryGetValue(userId, out var connections))
            {
                connections = new Dictionary<string, HubCallerContext>();
                _connectionsByUser[userId] = connections;
            }

            connections[context.ConnectionId] = context;
        }
    }

    public void Unregister(HubCallerContext context)
    {
        var userId = context.UserIdentifier;
        if (userId is null)
            return;

        lock (_gate)
        {
            if (!_connectionsByUser.TryGetValue(userId, out var connections))
                return;

            connections.Remove(context.ConnectionId);
            if (connections.Count == 0)
                _connectionsByUser.Remove(userId);
        }
    }

    public void CloseAll(string userId)
    {
        List<HubCallerContext> toClose;
        lock (_gate)
        {
            if (!_connectionsByUser.TryGetValue(userId, out var connections))
                return;

            toClose = [.. connections.Values];
        }

        // Aborted outside the lock: each abort leads to OnDisconnectedAsync, which calls Unregister.
        foreach (var context in toClose)
            context.Abort();
    }

    /// <summary>The number of users with at least one registered connection (for tests).</summary>
    internal int UserCount
    {
        get
        {
            lock (_gate)
                return _connectionsByUser.Count;
        }
    }
}
