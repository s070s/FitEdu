using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AssessmentSchemaInfoTests
{
    [Theory]
    [InlineData("""{"questions":[{"id":"q1"},{"id":"q2"}]}""", 2)]
    [InlineData("""{"sections":[{"questions":[{"id":"q1"}]},{"questions":[{"id":"q2"},{"id":"q3"}]}]}""", 3)]
    [InlineData("""{"sections":[{"id":"s1","questionIds":["q1"]}],"questions":[{"id":"q1"}]}""", 1)]
    [InlineData("not json", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void CountQuestions_FlatSectionedAndInvalidSchemas(string? schemaJson, int expected)
    {
        AssessmentSchemaInfo.CountQuestions(schemaJson).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(9, 5)]
    [InlineData(12, 6)]
    public void EstimatedMinutes_HalfAMinutePerQuestionRoundedUp(int questionCount, int expected)
    {
        AssessmentSchemaInfo.EstimatedMinutes(questionCount).ShouldBe(expected);
    }
}
