using System.Collections.Concurrent;
using FitEdu.Application.ApiInterfaces;
using Microsoft.AspNetCore.SignalR;

namespace FitEdu.Web.Services;

/// <summary>
/// Knows every open chat, call and notification hub connection per user, so an admin action that ends a
/// member's sessions can close them on purpose (<see cref="ILiveConnectionCloser"/>). The hubs register a
/// connection once their own checks pass and unregister it when it closes. Singleton: it outlives each hub
/// instance, which SignalR creates per call.
/// </summary>
public sealed class LiveConnectionRegistry : ILiveConnectionCloser
{
    // userId -> (connectionId -> context). The inner map is a ConcurrentDictionary too, so two connections of
    // one user can register and unregister at the same time.
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, HubCallerContext>> _connectionsByUser = new();

    public void Register(HubCallerContext context)
    {
        var userId = context.UserIdentifier;
        if (userId is null)
            return;

        var connections = _connectionsByUser.GetOrAdd(userId, _ => new ConcurrentDictionary<string, HubCallerContext>());
        connections[context.ConnectionId] = context;
    }

    public void Unregister(HubCallerContext context)
    {
        var userId = context.UserIdentifier;
        if (userId is null)
            return;

        if (_connectionsByUser.TryGetValue(userId, out var connections))
            connections.TryRemove(context.ConnectionId, out _);
    }

    public void CloseAll(string userId)
    {
        if (!_connectionsByUser.TryGetValue(userId, out var connections))
            return;

        foreach (var context in connections.Values)
            context.Abort();
    }
}
