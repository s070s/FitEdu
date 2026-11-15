namespace FitEdu.Application.DTOs;

/// <summary>
/// Payload of the short-lived signed DataProtection token exchanged between
/// <c>AuthService.CreateSignInToken</c> (producer, Infrastructure) and the
/// <c>/auth/complete</c> minimal endpoint (consumer, Web). Serialized as JSON inside the
/// protector so producer and consumer share one compile-time-checked shape instead of an
/// order-sensitive delimited string.
/// <see cref="TicketId"/> is unique per ticket: <c>/auth/complete</c> accepts each id once, so a sign-in
/// link that was already opened cannot be opened again (replay); a still-unopened link can be forwarded
/// within its 5 minutes. A ticket without an id (an empty one) is refused.
/// </summary>
public record AuthCompletionTicket(
    string UserId,
    string? AdminBackupId,
    string SecurityStamp,
    bool RememberMe,
    Guid TicketId = default);
