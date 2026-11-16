using FitEdu.Application.Common;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class AssessmentSchemaValidatorTests
{
    private static string Flat(params string[] questions) => $$"""{"questions":[{{string.Join(",", questions)}}]}""";

    private static string Question(string id, string type, string extra = "") =>
        $$"""{"id":"{{id}}","type":"{{type}}","label":"L"{{extra}}}""";

    private static AssessmentSchemaProblemKind[] KindsOf(string schemaJson) =>
        AssessmentSchemaValidator.Validate(schemaJson).Select(problem => problem.Kind).ToArray();

    [Fact]
    public void SectionedSchemaWithGoodQuestions_HasNoProblems()
    {
        var schema = """{"sections":[{"id":"s1","questions":[{"id":"q1","type":"choice","label":"A","options":["a","b"],"optionsEl":["α","β"]},{"id":"q2","type":"rating","label":"B","min":1,"max":5}]}]}""";

        AssessmentSchemaValidator.Validate(schema).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"foo":"bar"}""")]
    [InlineData("[]")]
    public void SchemaWithoutAQuestionList_IsNotASchema(string schema) =>
        KindsOf(schema).ShouldBe([AssessmentSchemaProblemKind.NotASchema]);

    [Fact]
    public void BlankQuestionId_IsAProblem() =>
        KindsOf(Flat(Question(" ", "text"))).ShouldBe([AssessmentSchemaProblemKind.BlankQuestionId]);

    [Fact]
    public void RepeatedQuestionId_IsAProblem_AcrossSections()
    {
        var schema = """{"sections":[{"id":"s1","questions":[{"id":"q1","type":"text"}]},{"id":"s2","questions":[{"id":"q1","type":"text"}]}]}""";

        var problem = AssessmentSchemaValidator.Validate(schema).ShouldHaveSingleItem();

        problem.Kind.ShouldBe(AssessmentSchemaProblemKind.DuplicateQuestionId);
        problem.QuestionId.ShouldBe("q1");
    }

    [Theory]
    [InlineData("choice")]
    [InlineData("multiselect")]
    [InlineData("single-select")]
    public void ChoiceWithoutOptions_IsAProblem(string type) =>
        KindsOf(Flat(Question("q1", type))).ShouldBe([AssessmentSchemaProblemKind.ChoiceWithoutOptions]);

    [Fact]
    public void GreekOptionsOfAnotherLength_AreAProblem() =>
        KindsOf(Flat(Question("q1", "choice", ",\"options\":[\"a\",\"b\"],\"optionsEl\":[\"α\"]")))
            .ShouldBe([AssessmentSchemaProblemKind.OptionsDiffer]);

    [Fact]
    public void ChoiceWithEnglishOptionsOnly_IsFine() =>
        KindsOf(Flat(Question("q1", "choice", ",\"options\":[\"a\",\"b\"]"))).ShouldBeEmpty();

    [Theory]
    [InlineData(",\"min\":1,\"max\":5", true)]
    [InlineData(",\"min\":0,\"max\":10", true)]
    [InlineData("", true)]
    [InlineData(",\"min\":5,\"max\":5", false)]
    [InlineData(",\"min\":-1,\"max\":5", false)]
    [InlineData(",\"min\":1,\"max\":11", false)]
    [InlineData(",\"min\":1,\"max\":1000000", false)]
    public void RatingScale_MustBeZeroToTenWithMinBelowMax(string scale, bool valid)
    {
        var kinds = KindsOf(Flat(Question("q1", "rating", scale)));

        kinds.ShouldBe(valid ? [] : [AssessmentSchemaProblemKind.RatingScaleInvalid]);
    }
}
