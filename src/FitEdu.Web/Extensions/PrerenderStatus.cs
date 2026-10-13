namespace FitEdu.Web.Extensions;

/// <summary>The HTTP status a prerendered page answers with.</summary>
public static class PrerenderStatus
{
    /// <summary>
    /// For a detail page that has no item to show: 404 when the API said the item does not exist,
    /// so search engines drop the address, or 503 when the API could not be asked (an outage), so
    /// they keep a real address and try again later. The page still shows its own view.
    /// </summary>
    public static void SetMissing(HttpContext? httpContext, bool requestFailed)
    {
        var statusCode = requestFailed ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status404NotFound;
        SetStatus(httpContext, statusCode);
    }

    /// <summary>
    /// Only the prerender has an HttpContext; the interactive render gets null and nothing changes
    /// once the response has started.
    /// </summary>
    private static void SetStatus(HttpContext? httpContext, int statusCode)
    {
        if (httpContext is null || httpContext.Response.HasStarted)
            return;

        // Set just before the headers go out, not now: Blazor drops the page body of a response
        // that is already 404 at the end of the render, leaving it to the app-wide not-found page.
        var response = httpContext.Response;
        response.OnStarting(() =>
        {
            response.StatusCode = statusCode;
            return Task.CompletedTask;
        });
    }
}
