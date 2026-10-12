using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Public testimonials endpoint — no authentication required.
/// Returns active testimonials ordered by DisplayOrder for the landing page.
/// </summary>
[ApiController]
[Route("api/testimonials")]
[Tags("Testimonials")]
[Produces("application/json", "application/problem+json")]
public class TestimonialsController : ControllerBase
{
    private readonly ITestimonialService _testimonials;

    public TestimonialsController(ITestimonialService testimonials)
    {
        _testimonials = testimonials;
    }

    /// <summary>Returns all active testimonials ordered by DisplayOrder, with role and quote in <paramref name="lang"/>.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminTestimonialDto>>> GetActive(
        [FromQuery] string lang = "en", CancellationToken cancellationToken = default)
    {
        var result = await _testimonials.GetActiveAsync(lang, cancellationToken);
        return Ok(result);
    }
}
