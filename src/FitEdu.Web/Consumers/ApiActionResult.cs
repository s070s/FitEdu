using System.Net;

namespace FitEdu.Web.Consumers;

/// <summary>
/// The outcome of a write call (POST, DELETE) for a page that must tell a refusal from an outage:
/// it succeeded, the API refused it (<see cref="StatusCode"/>, plus the problem-details
/// <see cref="Detail"/> text when the body had one), or the API could not be reached
/// (<see cref="StatusCode"/> is null).
/// </summary>
public sealed record ApiActionResult(bool Succeeded, HttpStatusCode? StatusCode = null, string? Detail = null)
{
    /// <summary>The API accepted the call.</summary>
    public static ApiActionResult Success { get; } = new(true);

    /// <summary>The API could not be reached: a refused connection or a timeout.</summary>
    public static ApiActionResult Unreachable { get; } = new(false);

    /// <summary>The API answered with an error status.</summary>
    public static ApiActionResult Refused(HttpStatusCode statusCode, string? detail = null) => new(false, statusCode, detail);
}
