using Microsoft.AspNetCore.SignalR;
using FitEdu.Web.Hubs;

namespace FitEdu.Web.Services;

/// <summary>
/// AVAIL-5: on graceful shutdown, tell connected chat/call clients the server is going down so
/// they get a clean, server-initiated notice instead of only discovering the drop via their own
/// reconnect/timeout logic. Clients that are in an active call tear it down cleanly on this event
/// (see CallService) rather than having the WebRTC mesh die abruptly. Runs during host StopAsync,
/// while the SignalR connections are still open, before the process exits.
/// </summary>
public sealed partial class GracefulShutdownNotifier : IHostedService
{
    private readonly IHubContext<ChatHub> _chatHub;
    private readonly IHubContext<CallHub> _callHub;
    private readonly ILogger<GracefulShutdownNotifier> _logger;

    public GracefulShutdownNotifier(
        IHubContext<ChatHub> chatHub,
        IHubContext<CallHub> callHub,
        ILogger<GracefulShutdownNotifier> logger)
    {
        _chatHub = chatHub;
        _callHub = callHub;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _callHub.Clients.All.SendAsync("ServerShuttingDown", cancellationToken);
            await _chatHub.Clients.All.SendAsync("ServerShuttingDown", cancellationToken);
        }
        catch (Exception ex)
        {
            // Best-effort courtesy notice — never let it hold up or fault the shutdown path.
            LogBroadcastShutdownFailed(_logger, ex);
        }
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to broadcast ServerShuttingDown to clients on shutdown.")]
    private static partial void LogBroadcastShutdownFailed(ILogger logger, Exception exception);
}
