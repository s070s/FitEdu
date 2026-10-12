using System.Text.Json.Nodes;
using FitEdu.Web.Shared;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// Unit tests for AssessmentSchemaMapper, the admin editor's schema reader and writer.
/// The seeded schemas' full round trip is covered by AdminAssessmentEditTests.
/// </summary>
public class AssessmentSchemaMapperTests
{
    [Fact]
    public void Parse_FlatSchemaWithOldEditorKeys_BecomesOneSection_AndSavesInTheSeedStyle()
    {
        const string oldEditorSchema = """
            {"id":"old_v1","title":"Old","version":"2.0","questions":[
              {"id":"q1","type":"multi-select","labelEn":"Pick some","labelEl":"Διάλεξε","optionsEn":["A","B"],"optionsEl":["Α","Β"],"required":true}
            ]}
            """;

        var schema = AssessmentSchemaMapper.Parse(oldEditorSchema);
        var saved = JsonNode.Parse(AssessmentSchemaMapper.Serialize(schema, "old_v1", "Old", null))!;

        schema.Sections.Count.ShouldBe(1);
        schema.Sections[0].Id.ShouldBe("s1");
        var question = saved["sections"]![0]!["questions"]![0]!;
        question["type"]!.GetValue<string>().ShouldBe("multi-select");
        question["label"]!.GetValue<string>().ShouldBe("Pick some");
        question["options"]!.AsArray().Select(option => option!.GetValue<string>()).ShouldBe(["A", "B"]);
        question["optionsEl"]!.AsArray().Select(option => option!.GetValue<string>()).ShouldBe(["Α", "Β"]);
        question["labelEn"].ShouldBeNull();
        saved["version"]!.GetValue<string>().ShouldBe("2.0");
    }

    [Fact]
    public void Serialize_LeavesOutBlankOptionalText_AndKeepsGreekReadable()
    {
        var schema = new EditableAssessmentSchema();
        var section = new EditableSection { Id = "s1", TitleEn = "Basics", TitleEl = "  " };
        section.Questions.Add(new EditableQuestion { Id = "q1", Type = AssessmentQuestionTypes.Rating, LabelEn = "Energy", LabelEl = "Ενέργεια", MinLabelEn = "Low", MaxLabelEl = " " });
        schema.Sections.Add(section);

        var json = AssessmentSchemaMapper.Serialize(schema, "basics_v1", "Basics", "Βασικά");
        var question = JsonNode.Parse(json)!["sections"]![0]!["questions"]![0]!.AsObject();

        json.ShouldContain("Ενέργεια");
        JsonNode.Parse(json)!["sections"]![0]!.AsObject().ContainsKey("titleEl").ShouldBeFalse();
        question.Select(property => property.Key).ShouldBe(["id", "type", "label", "labelEl", "min", "max", "minLabel", "required"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    public void Parse_EmptyOrInvalidJson_GivesNoSections(string? schemaJson)
    {
        AssessmentSchemaMapper.Parse(schemaJson).Sections.ShouldBeEmpty();
    }

    [Fact]
    public void NextQuestionId_IsUnusedInEverySection()
    {
        var schema = new EditableAssessmentSchema();
        var first = new EditableSection { Id = "s1" };
        first.Questions.Add(new EditableQuestion { Id = "q1" });
        first.Questions.Add(new EditableQuestion { Id = "q3" });
        var second = new EditableSection { Id = "s2" };
        second.Questions.Add(new EditableQuestion { Id = "q2" });
        schema.Sections.Add(first);
        schema.Sections.Add(second);

        schema.NextQuestionId().ShouldBe("q4");
        schema.NextSectionId().ShouldBe("s3");
    }
}
