using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Controllers;

/// <summary>
/// The public home page's numbers and showcase courses in one anonymous call, so the guest home
/// page needs no separate catalogue (<see cref="CoursesController"/>) and count queries.
/// </summary>
[ApiController]
[Route("api/landing")]
[AllowAnonymous]
[Tags("Landing")]
[Produces("application/json")]
public class LandingController(ILandingService landingService) : ControllerBase
{
    /// <summary>
    /// Published course and lesson counts, the exercise library size, the member count, the most-enrolled
    /// courses and whether a custom hero photo is set.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<LandingOverviewDto>> GetOverview(
        [FromQuery] string lang = "en", CancellationToken cancellationToken = default)
    {
        var overview = await landingService.GetOverviewAsync(lang, cancellationToken);
        return Ok(overview);
    }
}
