using System.Text.Json;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// bUnit tests for the admin assessment editor: opening a seeded assessment and saving it
/// unchanged must send back the same schema (sections, ids, types, Greek text, options and rating labels).
/// </summary>
public class AdminAssessmentEditTests : BunitContext
{
    private static readonly string AssessmentsFolder = Path.Combine(RepoPaths.SeedJsonRoot, "Assessments");

    private readonly IAdminAssessmentConsumer _assessmentConsumer = Substitute.For<IAdminAssessmentConsumer>();
    private readonly IAdminCategoryConsumer _categoryConsumer = Substitute.For<IAdminCategoryConsumer>();
    private SaveAssessmentDefinitionRequest? _savedRequest;

    public AdminAssessmentEditTests()
    {
        Services.AddSingleton(_assessmentConsumer);
        Services.AddSingleton(_categoryConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The rich-text editors and the unsaved-changes guard call JS; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;

        _categoryConsumer.GetAllCategoriesAsync().Returns([]);
        _assessmentConsumer
            .UpdateAssessmentAsync(Arg.Any<Guid>(), Arg.Do<SaveAssessmentDefinitionRequest>(request => _savedRequest = request))
            .Returns(call => Assessment(call.ArgAt<Guid>(0), "saved", "Saved", null, "{}"));
    }

    /// <summary>Every seeded assessment file name, one theory row each.</summary>
    public static TheoryData<string> SeedAssessmentFiles()
    {
        var files = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(AssessmentsFolder, "*.json").Order(StringComparer.Ordinal))
            files.Add(Path.GetFileName(path));
        return files;
    }

    private static AdminAssessmentDefinitionDto Assessment(Guid id, string key, string titleEn, string? titleEl, string schemaJson) =>
        new(id, key, titleEn, titleEl, null, null, schemaJson, true, DateTimeOffset.UtcNow, null, null);

    [Theory]
    [MemberData(nameof(SeedAssessmentFiles))]
    public void AdminAssessmentEdit_RoundTrip_PreservesSections(string fileName)
    {
        using var seedFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(AssessmentsFolder, fileName)));
        var seed = seedFile.RootElement;
        var originalSchema = seed.GetProperty("SchemaJson").GetString()!;
        var id = Guid.NewGuid();
        _assessmentConsumer.GetAssessmentAsync(id).Returns(Assessment(
            id,
            seed.GetProperty("Key").GetString()!,
            seed.GetProperty("Title").GetString()!,
            seed.GetProperty("TitleEl").GetString(),
            originalSchema));

        var page = Render<AdminAssessmentEdit>(parameters => parameters.Add(p => p.AssessmentId, id));
        page.FindAll("button").First(button => button.TextContent.Trim() == AdminRes.SaveAssessment).Click();

        _savedRequest.ShouldNotBeNull();
        var savedSchema = JsonNode.Parse(_savedRequest.SchemaJson);
        JsonNode.DeepEquals(JsonNode.Parse(originalSchema), savedSchema)
            .ShouldBeTrue($"{fileName} changed on save. Saved schema: {savedSchema?.ToJsonString()}");
    }
}
