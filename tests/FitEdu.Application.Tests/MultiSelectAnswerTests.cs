using FitEdu.Application.Common;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class MultiSelectAnswerTests
{
    [Fact]
    public void Serialize_OptionsWithCommas_RoundTripWhole()
    {
        var stored = MultiSelectAnswer.Serialize(["Chest pain, pressure or discomfort", "Πόνος στο στήθος"]);

        stored.ShouldBe("""["Chest pain, pressure or discomfort","Πόνος στο στήθος"]""");
        MultiSelectAnswer.TryParse(stored).ShouldBe(["Chest pain, pressure or discomfort", "Πόνος στο στήθος"]);
    }

    [Fact]
    public void Serialize_NoOptions_IsEmpty()
    {
        MultiSelectAnswer.Serialize([]).ShouldBe(string.Empty);
    }

    [Fact]
    public void ToDisplayText_JsonArray_JoinsTheOptionsWithAMiddleDot()
    {
        var text = MultiSelectAnswer.ToDisplayText("""["Walks, hikes","Yoga"]""");

        text.ShouldBe("Walks, hikes · Yoga");
    }

    [Theory]
    [InlineData("Walks,Yoga")]          // stored before the JSON format: shown as it was saved
    [InlineData("[not json")]           // a plain answer that only starts with a bracket
    [InlineData("4")]
    [InlineData("")]
    public void ToDisplayText_OtherText_IsShownAsItIs(string answer)
    {
        MultiSelectAnswer.ToDisplayText(answer).ShouldBe(answer);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("   ", true)]
    [InlineData("[]", true)]
    [InlineData("""["Yoga"]""", false)]
    [InlineData("Walks,Yoga", false)]
    public void IsBlank_TreatsAnEmptyListAsNoAnswer(string? answer, bool expected)
    {
        MultiSelectAnswer.IsBlank(answer).ShouldBe(expected);
    }
}
