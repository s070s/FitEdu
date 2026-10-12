using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// Persists call session/participant lifecycle and the chat-history events that accompany 1:1 calls.
/// </summary>
public interface ICallEventService
{
    Task<CallSession> CreateSessionAsync(string initiatorId, Guid? conversationId, IEnumerable<string> inviteeIds);
    Task MarkParticipantAsync(Guid callId, string userId, CallParticipantStatus status);
    Task EndSessionAsync(Guid callId, CallEndReason reason);
    Task<ChatMessageDto?> RecordChatEventAsync(Guid callId, CallEventKind kind);
}
