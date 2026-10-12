using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>Client consumer for the public learning-path catalog.</summary>
public interface IPathConsumer
{
    Task<IReadOnlyList<LearningPathListItemDto>> GetPathsAsync(string lang = "en");
    Task<LearningPathDetailDto?> GetPathAsync(Guid id, string lang = "en");
}
