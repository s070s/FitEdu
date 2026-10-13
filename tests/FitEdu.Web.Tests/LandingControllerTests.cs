using System.Net;
using System.Net.Http.Json;
using FitEdu.Application.DTOs;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>The public home page API answers guests, who cannot read the member-only course catalog.</summary>
public class LandingControllerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public LandingControllerTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetOverview_Anonymous_ReturnsTheNumbers()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/landing?lang=el");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var overview = await response.Content.ReadFromJsonAsync<LandingOverviewDto>();
        overview.ShouldNotBeNull();
        overview.FeaturedCourses.ShouldNotBeNull();
    }
}
