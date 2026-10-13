using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class CrossCuttingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CrossCuttingTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ProtectedEndpoint_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/courses");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminEndpoint_Student_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.GetAsync("/api/admin/courses");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AdminEndpoint_Admin_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync("/api/admin/courses");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_IncludeSecurityHeaders()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/blog/summaries");

        response.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        response.Headers.Contains("X-Frame-Options").ShouldBeTrue();
        response.Headers.Contains("Referrer-Policy").ShouldBeTrue();
        response.Headers.Contains("Permissions-Policy").ShouldBeTrue();
    }

    [Fact]
    public async Task UnknownRoute_RendersNotFoundPage_NotBlank()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/this-route-does-not-exist-e2e");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain("Page not found");
        // The home hero is the page a broken route used to fall back to.
        body.ShouldNotContain("hero-title");
    }

    [Fact]
    public async Task UnknownRoute_RendersInsideTheAppShell_WithItsOwnTitle()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/this-route-does-not-exist-e2e");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // A bare component fragment has no head: no styles, no navigation and an empty tab title.
        body.ShouldContain("<title>Page not found</title>");
        body.ShouldContain("css/app.css");
        body.ShouldContain("_framework/blazor.web.js");
    }

    [Fact]
    public async Task UnknownApiRoute_StaysJsonProblemDetails()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/this-endpoint-does-not-exist-e2e");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldNotContain("Page not found");
    }

    [Fact]
    public async Task ServiceResultFailure_ReturnsProblemDetailsBody()
    {
        // A ServiceResult failure flows through ToActionResult (API-1): the body must now be a
        // machine-readable RFC 7807 ProblemDetails (status + traceId extension) instead of the raw
        // text/plain string it used to be. (The content-type header stays application/json here
        // because the controller carries [Produces("application/json")], which overrides the
        // problem+json media type — the envelope shape is what a client parses.)
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        var response = await client.GetAsync($"/api/assessments/{Guid.NewGuid()}");
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        response.IsSuccessStatusCode.ShouldBeFalse();
        problem.ShouldNotBeNull();
        problem!.Status.ShouldBe((int)response.StatusCode);
        problem.Extensions.ShouldContainKey("traceId");
    }

    [Fact]
    public async Task ConvertedStringError_ReturnsProblemJson()
    {
        // MediaController rejected bad paths with a text/plain string body before API-1; it must now
        // be problem+json carrying the message in ProblemDetails.detail.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/media/bad..path");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        body.ShouldContain("Invalid file path.");
    }
}
