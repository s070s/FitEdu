using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Reads the authorization metadata of every mapped API endpoint, so a new admin action that forgot its
/// policy, or a new anonymous endpoint nobody listed, fails here instead of going live (P03-3). The
/// per-URL 401/403 tests only reach a handful of the actions.
/// </summary>
public class EndpointAuthorizationTests : IClassFixture<CustomWebAppFactory>
{
    private const string AdminPolicy = "AdminOnly";

    // The landing page background image: every guest's page loads it, so it is the one SiteSettings action open to all.
    private static readonly string[] AnonymousAdminSurfaceEndpoints = ["GET api/site/background-image"];

    // The anonymous API surface, the README's "Anonymous reads" row plus the sign-in family, the media and lesson-asset
    // files every page embeds, the public certificate check and the payment provider's signed webhook. Adding an
    // anonymous endpoint means listing it here.
    private static readonly string[] AnonymousApiEndpoints =
    [
        "GET api/blog/summaries",
        "GET api/blog/{slug}",
        "GET api/categories",
        "GET api/certificates/verify/{verificationId:guid}",
        "GET api/courses",
        "GET api/courses/{courseId:guid}",
        "GET api/courses/{courseId:guid}/reviews",
        "GET api/exercises",
        "GET api/exercises/facets",
        "GET api/exercises/{slug}",
        "GET api/exercises/{slug}/lessons",
        "GET api/landing",
        "GET api/lessons/{lessonId:guid}/asset",
        "GET api/media/{*filePath}",
        "GET api/paths",
        "GET api/paths/{id:guid}",
        "GET api/search",
        "GET api/site/background-image",
        "GET api/subscriptions/plans",
        "GET api/testimonials",
        "POST api/auth/confirm-email",
        "POST api/auth/forgot-password",
        "POST api/auth/login",
        "POST api/auth/refresh",
        "POST api/auth/register",
        "POST api/auth/resend-confirmation",
        "POST api/auth/reset-password",
        "POST api/subscriptions/webhook",
#if DEBUG
        // Compiled into Debug builds only: the e2e suite's sign-in fixture (the action itself refuses outside Development).
        "POST api/auth/dev/reset-password",
#endif
    ];

    private readonly CustomWebAppFactory _factory;

    public EndpointAuthorizationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public void EveryAdminEndpoint_RequiresTheAdminPolicy_AndIsNotAnonymous()
    {
        var unprotected = ApiEndpoints()
            .Where(endpoint => IsAdminSurface(endpoint))
            .Where(endpoint => !AnonymousAdminSurfaceEndpoints.Contains(endpoint.Name))
            .Where(endpoint => !RequiresAdminPolicy(endpoint) || IsAnonymous(endpoint))
            .Select(endpoint => endpoint.Name)
            .ToList();

        unprotected.ShouldBeEmpty("these admin endpoints are not behind the AdminOnly policy: " + string.Join(", ", unprotected));
    }

    [Fact]
    public void TheAdminSurface_IsActuallyScanned()
    {
        var adminEndpoints = ApiEndpoints().Where(endpoint => IsAdminSurface(endpoint)).ToList();

        // A refactor of the controller names or routes must not turn the scan above into an empty pass.
        adminEndpoints.Count.ShouldBeGreaterThan(60);
        adminEndpoints.ShouldContain(endpoint => endpoint.Name == "POST api/admin/site/background-image");
        adminEndpoints.ShouldContain(endpoint => endpoint.Name == "GET api/admin/courses");
    }

    [Fact]
    public void EveryApiEndpoint_RequiresAuthorization_OrIsOnTheAnonymousList()
    {
        var anonymous = ApiEndpoints()
            .Where(endpoint => IsAnonymous(endpoint) || !RequiresAuthorization(endpoint))
            .Select(endpoint => endpoint.Name)
            .Order()
            .ToList();

        anonymous.ShouldBe(AnonymousApiEndpoints.Order().ToList());
    }

    private IEnumerable<ApiEndpoint> ApiEndpoints()
    {
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        foreach (var endpoint in endpoints)
        {
            var route = endpoint.RoutePattern.RawText ?? string.Empty;
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            if (!route.StartsWith("api/", StringComparison.Ordinal))
            {
                continue;
            }

            var controllerName = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.Name ?? string.Empty;
            foreach (var method in methods)
            {
                yield return new ApiEndpoint($"{method} {route}", route, controllerName, endpoint);
            }
        }
    }

    // The Admin* controllers, SiteSettings (it mixes the one public read with the admin uploads) and anything routed under api/admin.
    private static bool IsAdminSurface(ApiEndpoint endpoint) =>
        endpoint.ControllerName.StartsWith("Admin", StringComparison.Ordinal) ||
        endpoint.ControllerName == "SiteSettingsController" ||
        endpoint.Route.StartsWith("api/admin/", StringComparison.Ordinal);

    private static bool RequiresAdminPolicy(ApiEndpoint endpoint) =>
        endpoint.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == AdminPolicy);

    private static bool RequiresAuthorization(ApiEndpoint endpoint) =>
        endpoint.Endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;

    private static bool IsAnonymous(ApiEndpoint endpoint) =>
        endpoint.Endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;

    private sealed record ApiEndpoint(string Name, string Route, string ControllerName, RouteEndpoint Endpoint);
}
