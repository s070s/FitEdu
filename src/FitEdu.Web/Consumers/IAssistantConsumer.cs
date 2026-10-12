using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>
/// Client consumer for the AI assistant API.
/// </summary>
public interface IAssistantConsumer
{
    IAsyncEnumerable<AssistantStreamEvent> StreamChatAsync(AssistantChatRequest request, string lang, CancellationToken cancellationToken = default);
    Task<AssistantStatusDto?> GetStatusAsync();
}
