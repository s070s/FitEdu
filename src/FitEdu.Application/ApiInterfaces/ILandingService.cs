using FitEdu.Application.DTOs;

namespace FitEdu.Application.ApiInterfaces;

/// <summary>
/// The public home page's numbers (courses, lessons, exercises, members), its showcase courses and
/// whether an admin set a custom hero photo. Anonymous: nothing in it depends on the visitor.
/// </summary>
public interface ILandingService
{
    Task<LandingOverviewDto> GetOverviewAsync(string lang, CancellationToken cancellationToken = default);
}
