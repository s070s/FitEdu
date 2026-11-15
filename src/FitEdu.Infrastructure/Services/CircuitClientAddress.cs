namespace FitEdu.Infrastructure.Services;

/// <summary>
/// The network address of the browser behind one Blazor circuit, as the text <see cref="AuthAttemptLimiter"/>
/// counts attempts by. Register as scoped (one per circuit). The circuit handler sets it once when the circuit
/// opens, because <c>HttpContext</c> is not available to the page code that runs later inside the circuit.
/// </summary>
public sealed class CircuitClientAddress
{
    /// <summary>
    /// The address text, or null when it is not known or is a loopback address (the limiter then counts the
    /// circuit in its shared "unknown" partition).
    /// </summary>
    public string? Key { get; set; }
}
