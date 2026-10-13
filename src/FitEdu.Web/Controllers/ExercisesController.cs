using Microsoft.AspNetCore.Mvc;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources.Messages;

namespace FitEdu.Web.Controllers;

/// <summary>
/// Public exercise library — no authentication required, like <see cref="BlogController"/>.
/// Only published exercises are visible. The "used in lessons" list is public too: it shows only
/// lesson and course titles, which the public course catalogue (<see cref="CoursesController"/>)
/// already shows to guests.
/// </summary>
[ApiController]
[Route("api/exercises")]
[Tags("Exercises")]
[Produces("application/json", "application/problem+json")]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public class ExercisesController(IExerciseService exerciseService) : ControllerBase
{
    /// <summary>
    /// Get a paged, filtered list of published exercises ordered by name.
    /// Muscle, equipment, level and category take the exact values returned by /facets.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<ExerciseListItemDto>>> GetExercises(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = ExerciseLibraryPaging.DefaultPageSize,
        [FromQuery] string lang = "en",
        [FromQuery] string? search = null,
        [FromQuery] string? muscle = null,
        [FromQuery] string? equipment = null,
        [FromQuery] string? level = null,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = PagingParams.Normalize(page, pageSize);

        var filter = new ExerciseFilterDto(search, muscle, equipment, level, category);
        var result = await exerciseService.GetPublishedExercisesAsync(page, pageSize, lang, filter, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Get every muscle, equipment, level and category value with its number of published exercises.
    /// </summary>
    [HttpGet("facets")]
    public async Task<ActionResult<ExerciseFacetsDto>> GetFacets(CancellationToken cancellationToken = default)
    {
        var facets = await exerciseService.GetFacetsAsync(cancellationToken);
        return Ok(facets);
    }

    /// <summary>
    /// Get one published exercise with its instructions.
    /// </summary>
    [HttpGet("{slug}")]
    public async Task<ActionResult<ExerciseDetailDto>> GetExercise(
        string slug, [FromQuery] string lang = "en", CancellationToken cancellationToken = default)
    {
        var dto = await exerciseService.GetPublishedBySlugAsync(slug, lang, cancellationToken);
        return dto is not null
            ? Ok(dto)
            : Problem(detail: ErrorMessagesRes.Exercise_NotFound, statusCode: StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Get the lessons of published courses that prescribe this exercise (empty for an unknown slug).
    /// </summary>
    [HttpGet("{slug}/lessons")]
    public async Task<ActionResult<IReadOnlyList<LessonUsingExerciseDto>>> GetLessonsUsingExercise(
        string slug, [FromQuery] string lang = "en", CancellationToken cancellationToken = default)
    {
        var lessons = await exerciseService.GetLessonsUsingExerciseAsync(slug, lang, cancellationToken);
        return Ok(lessons);
    }
}
