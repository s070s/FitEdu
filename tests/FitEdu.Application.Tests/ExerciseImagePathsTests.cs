using FitEdu.Application.Common;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

public class ExerciseImagePathsTests
{
    [Fact]
    public void Url_PointsAtTheExerciseImageFolder()
    {
        ExerciseImagePaths.Url("pushups", 1).ShouldBe("/images/exercises/pushups/1.webp");
    }

    [Fact]
    public void EndUrl_WithOnlyOneImage_FallsBackToTheStartImage()
    {
        ExerciseImagePaths.EndUrl("pushups", imageCount: 2).ShouldBe("/images/exercises/pushups/1.webp");
        ExerciseImagePaths.EndUrl("pushups", imageCount: 1).ShouldBe("/images/exercises/pushups/0.webp");
    }

    [Fact]
    public void CourseCoverUrl_UsesTheCoverExerciseStartImage_OrNullWithoutOne()
    {
        ExerciseImagePaths.CourseCoverUrl("plank").ShouldBe("/images/exercises/plank/0.webp");
        ExerciseImagePaths.CourseCoverUrl(null).ShouldBeNull();
        ExerciseImagePaths.CourseCoverUrl(" ").ShouldBeNull();
    }
}
