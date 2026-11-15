using Microsoft.AspNetCore.Components.Server.Circuits;
using FitEdu.Infrastructure.Services;
using FitEdu.Web.Startup;

namespace FitEdu.Web.Services;

/// <summary>
/// Copies the browser's network address into the circuit's <see cref="CircuitClientAddress"/> when the circuit
/// opens: the SignalR connection's HttpContext is only available then, not to the page code that runs later.
/// A loopback or missing address is stored as null, so <see cref="AuthAttemptLimiter"/> counts it in its shared
/// "unknown" partition. Register as scoped, so it shares the circuit's <see cref="CircuitClientAddress"/>.
/// </summary>
public sealed class ClientAddressCircuitHandler(
    IHttpContextAccessor httpContextAccessor,
    CircuitClientAddress clientAddress) : CircuitHandler
{
    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var remoteAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        clientAddress.Key = PublicRateLimit.PartitionKeyFor(remoteAddress);
        return Task.CompletedTask;
    }
}
