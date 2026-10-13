using FitEdu.Web.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// A prerendered detail page answers 404 only when the item is really missing, and 503 when the
/// API could not be asked, so crawlers drop dead addresses but keep real ones through an outage.
/// </summary>
public class PrerenderStatusTests
{
    [Fact]
    public async Task SetMissing_NotFound_Answers404()
    {
        var (httpContext, response) = PrerenderHttpContext.Create();

        PrerenderStatus.SetMissing(httpContext, requestFailed: false);
        await response.StartAsync();

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task SetMissing_FailedRequest_Answers503()
    {
        var (httpContext, response) = PrerenderHttpContext.Create();

        PrerenderStatus.SetMissing(httpContext, requestFailed: true);
        await response.StartAsync();

        httpContext.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
    }

    [Fact]
    public void SetMissing_InteractiveRender_DoesNothing()
    {
        // The interactive render has no HttpContext.
        Should.NotThrow(() => PrerenderStatus.SetMissing(null, requestFailed: true));
    }
}

/// <summary>An HttpContext whose response runs its OnStarting callbacks when the test says it starts.</summary>
public static class PrerenderHttpContext
{
    public static (HttpContext HttpContext, StartableResponseFeature Response) Create()
    {
        var response = new StartableResponseFeature();
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<IHttpResponseFeature>(response);
        return (httpContext, response);
    }
}

/// <summary>Keeps the OnStarting callbacks and runs them on <see cref="StartAsync"/>, as the server does.</summary>
public sealed class StartableResponseFeature : HttpResponseFeature
{
    private readonly List<(Func<object, Task> Callback, object State)> _startingCallbacks = [];
    private bool _hasStarted;

    public override bool HasStarted => _hasStarted;

    public override void OnStarting(Func<object, Task> callback, object state) => _startingCallbacks.Add((callback, state));

    public async Task StartAsync()
    {
        foreach (var (callback, state) in _startingCallbacks)
            await callback(state);

        _hasStarted = true;
    }
}
