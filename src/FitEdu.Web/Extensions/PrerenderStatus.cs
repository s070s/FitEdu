namespace FitEdu.Web.Extensions;

/// <summary>The HTTP status a prerendered page answers with.</summary>
public static class PrerenderStatus
{
    /// <summary>
    /// Makes a page that found nothing answer 404, so search engines drop the address, while it
    /// still shows its own friendly not-found view. Only the prerender has an HttpContext; the
    /// interactive render gets null and nothing changes once the response has started.
    /// </summary>
    public static void SetNotFound(HttpContext? httpContext)
    {
        if (httpContext is null || httpContext.Response.HasStarted)
            return;

        // Set just before the headers go out, not now: Blazor drops the page body of a response
        // that is already 404 at the end of the render, leaving it to the app-wide not-found page.
        var response = httpContext.Response;
        response.OnStarting(() =>
        {
            response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        });
    }
}
