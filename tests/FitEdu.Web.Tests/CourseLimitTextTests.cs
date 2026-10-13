using FitEdu.Web.Extensions;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The plan course-limit line: unlimited plans, a one-course plan in the singular (Greek and English
/// plurals do not agree with 1), and a counted format for everything else.
/// </summary>
public class CourseLimitTextTests
{
    private const string Unlimited = "Unlimited";
    private const string Single = "1 active course";
    private const string CountFormat = "Up to {0} active courses";

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(CourseLimitText.UnlimitedThreshold)]
    public void Describe_HugeLimit_IsUnlimited(int maxCourses)
    {
        CourseLimitText.Describe(maxCourses, Unlimited, Single, CountFormat).ShouldBe(Unlimited);
    }

    [Fact]
    public void Describe_OneCourse_UsesTheSingularLabel()
    {
        CourseLimitText.Describe(1, Unlimited, Single, CountFormat).ShouldBe(Single);
    }

    [Fact]
    public void Describe_SeveralCourses_FillsTheCount()
    {
        CourseLimitText.Describe(10, Unlimited, Single, CountFormat).ShouldBe("Up to 10 active courses");
    }
}
