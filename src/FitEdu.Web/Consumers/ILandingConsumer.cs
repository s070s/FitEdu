using FitEdu.Application.DTOs;

namespace FitEdu.Web.Consumers;

/// <summary>Client consumer for the public home page API.</summary>
public interface ILandingConsumer
{
    /// <summary>The landing numbers and showcase courses, or null when the API is unavailable.</summary>
    Task<LandingOverviewDto?> GetOverviewAsync(string lang = "en");
}
