using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.DTOs;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Category discovery for browse/filter chips. The read is public, because the course catalogue
/// that shows the chips is open to guests.
/// </summary>
[ApiController]
[Route("api/categories")]
[Authorize]
[Tags("Categories")]
[Produces("application/json", "application/problem+json")]
public class CategoriesController(ICategoryService categoryService) : ControllerBase
{
    /// <summary>
    /// Get categories that have at least one published item in the given scope, with counts.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<List<CategoryDto>>> GetCategories(
        [FromQuery] string scope = "courses",
        [FromQuery] string lang = "en",
        CancellationToken cancellationToken = default)
    {
        var result = await categoryService.GetCategoriesAsync(scope, lang, cancellationToken);
        return Ok(result);
    }
}
