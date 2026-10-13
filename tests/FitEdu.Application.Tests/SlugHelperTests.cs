using FitEdu.Application.Common;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class SlugHelperTests
{
    [Theory]
    [InlineData("3_4_Sit-Up", "3-4-sit-up")]
    [InlineData("Alternate_Incline_Dumbbell_Curl", "alternate-incline-dumbbell-curl")]
    [InlineData("90/90 Hamstring", "90-90-hamstring")]
    [InlineData("Barbell_Bench_Press_-_Medium_Grip", "barbell-bench-press-medium-grip")]
    [InlineData("_Leading_And_Trailing_", "leading-and-trailing")]
    public void FromExerciseId_ProducesLowerCaseDashedSlug(string exerciseId, string expectedSlug)
    {
        SlugHelper.FromExerciseId(exerciseId).ShouldBe(expectedSlug);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("___")]
    public void FromExerciseId_WithoutLettersOrDigits_ReturnsEmpty(string? exerciseId)
    {
        SlugHelper.FromExerciseId(exerciseId).ShouldBe(string.Empty);
    }
}
