using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AdminUsersIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AdminUsersIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetUsers_Admin_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUsers_Admin_IncludesSubscriptionTier()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync("/api/admin/users");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("subscriptionTier");
    }

    [Fact]
    public async Task GetUsers_Student_Returns403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync("/api/admin/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUser_Unknown_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        (await client.GetAsync("/api/admin/users/does-not-exist")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetUser_Admin_ShowsTheTrainingGoal()
    {
        var (_, userId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await _factory.SeedAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Goal = FitnessGoal.FatLoss;
            await db.SaveChangesAsync();
        });
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        var detail = await admin.GetFromJsonAsync<AdminUserDetailDto>($"/api/admin/users/{userId}");

        detail!.Goal.ShouldBe(nameof(FitnessGoal.FatLoss));
    }

    [Fact]
    public async Task GetRoles_Admin_Returns200()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.GetAsync("/api/admin/roles");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("Student");
    }

    [Fact]
    public async Task DisableUser_Unknown_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await client.PostAsync("/api/admin/users/does-not-exist/disable", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
