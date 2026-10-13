using System.Text.Json;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The OpenAPI document (served in Development, as the test host runs) pre-fills Swagger UI with
/// request examples; they describe the fitness platform and use the real assessment schema shape.
/// </summary>
public class OpenApiDocumentTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public OpenApiDocumentTests(CustomWebAppFactory factory) => _factory = factory;

    private async Task<JsonElement> LoadSchemasAsync()
    {
        var client = _factory.CreateClient();
        var json = await client.GetStringAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
    }

    private static JsonElement ExampleOf(JsonElement schemas, string schemaName) =>
        schemas.GetProperty(schemaName).GetProperty("example");

    [Fact]
    public async Task RequestExamples_DescribeFitnessContent()
    {
        var schemas = await LoadSchemasAsync();

        ExampleOf(schemas, "SaveCourseRequest").GetProperty("titleEn").GetString().ShouldBe("Strength Foundations");
        ExampleOf(schemas, "SaveLessonRequest").GetProperty("titleEn").GetString().ShouldBe("Full-body workout A");
        ExampleOf(schemas, "SaveBlogArticleRequest").GetProperty("slug").GetString().ShouldBe("progressive-overload-explained");
        ExampleOf(schemas, "SaveAssessmentDefinitionRequest").GetProperty("key").GetString().ShouldBe("training_readiness_v1");

        var allExamples = string.Join("\n", schemas.EnumerateObject()
            .Where(schema => schema.Value.TryGetProperty("example", out _))
            .Select(schema => schema.Value.GetProperty("example").GetRawText()));
        allExamples.ShouldNotContain("resilience", Case.Insensitive);
        allExamples.ShouldNotContain("burnout", Case.Insensitive);
        allExamples.ShouldNotContain("mentor", Case.Insensitive);
    }

    [Fact]
    public async Task AssessmentExample_UsesTheSeededSchemaShapeAndMatchingAnswers()
    {
        var schemas = await LoadSchemasAsync();

        var schemaJson = ExampleOf(schemas, "SaveAssessmentDefinitionRequest").GetProperty("schemaJson").GetString();
        using var schema = JsonDocument.Parse(schemaJson.ShouldNotBeNull());
        var question = schema.RootElement.GetProperty("sections")[0].GetProperty("questions")[0];
        question.GetProperty("id").GetString().ShouldBe("q1");
        question.GetProperty("type").GetString().ShouldBe("rating");
        question.GetProperty("labelEl").GetString().ShouldNotBeNullOrWhiteSpace();

        var answersJson = ExampleOf(schemas, "SubmitAssessmentRequest").GetProperty("answersJson").GetString();
        using var answers = JsonDocument.Parse(answersJson.ShouldNotBeNull());
        answers.RootElement.GetProperty("q1").GetString().ShouldBe("4"); // the form sends every answer as text
    }
}
