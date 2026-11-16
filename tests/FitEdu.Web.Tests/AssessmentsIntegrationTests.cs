using System.Net;
using System.Net.Http.Json;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AssessmentsIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AssessmentsIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAssessments_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/assessments")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAssessments_FreeUserWithoutAccess_Returns403()
    {
        // A freshly-seeded Student has no subscription → Free defaults (AssessmentAccess = false).
        var client = await _factory.CreateAuthenticatedClientAsync("Student");

        (await client.GetAsync("/api/assessments")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMine_Anonymous_Returns401()
    {
        var client = _factory.CreateClient();

        (await client.GetAsync("/api/assessments/mine")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static string SchemaWith(string questionId, string label) =>
        $$"""{"id":"x","version":"1.0","sections":[{"id":"s1","title":"S","questions":[{"id":"{{questionId}}","type":"text","label":"{{label}}","required":false}]}]}""";

    private static SaveAssessmentDefinitionRequest Save(string key, string schemaJson) =>
        new(key, "Cache test", null, null, null, schemaJson);

    [Fact]
    public async Task GetAssessment_AfterTheAdminChangesTheSchema_ShowsTheNewQuestion()
    {
        // P07-1: the resolved schema is cached for 30 minutes; an admin edit used to leave the old
        // questions on the form while the submit check already used the new ones.
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");
        var student = await _factory.CreateAuthenticatedClientWithPlanAsync("Student", SubscriptionTier.Plus);
        var created = await (await admin.PostAsJsonAsync("/api/admin/assessments", Save($"cache-{Guid.NewGuid():N}", SchemaWith("q1", "First question"))))
            .Content.ReadFromJsonAsync<AdminAssessmentDefinitionDto>();
        (await admin.PostAsync($"/api/admin/assessments/{created!.Id}/publish", null)).EnsureSuccessStatusCode();

        var before = await student.GetFromJsonAsync<AssessmentDefinitionDto>($"/api/assessments/{created.Id}?lang=en");
        before!.SchemaJson.ShouldContain("First question");

        var update = await admin.PutAsJsonAsync($"/api/admin/assessments/{created.Id}", Save(created.Key, SchemaWith("q2", "Second question")));
        update.EnsureSuccessStatusCode();

        var after = await student.GetFromJsonAsync<AssessmentDefinitionDto>($"/api/assessments/{created.Id}?lang=en");
        after!.SchemaJson.ShouldContain("Second question");
        after.SchemaJson.ShouldNotContain("First question");
    }
}
