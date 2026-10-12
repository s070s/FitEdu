using System.Text.Json;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Entities;

namespace FitEdu.Application.Mappings;

/// <summary>
/// Shared notification mapper (MAINT-1). Promoted from NotificationService's private helper so
/// NotificationDispatcher stops re-mapping the seven fields by hand for the SignalR push.
/// </summary>
public static class NotificationMappings
{
    /// <summary>Materialized-only: the BodyArgsJson deserialization can't translate to SQL.</summary>
    public static NotificationDto ToDto(this Notification n) =>
        new(n.Id,
            n.Type.ToString(),
            n.TitleKey,
            n.BodyArgsJson == null ? [] : JsonSerializer.Deserialize<List<string>>(n.BodyArgsJson) ?? [],
            n.LinkUrl,
            n.IsRead,
            n.CreatedAt);
}
