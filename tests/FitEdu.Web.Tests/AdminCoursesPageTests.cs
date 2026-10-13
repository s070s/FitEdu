using System.Globalization;
using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The admin programme list shows each title and category in the admin's UI language, falling back
/// to English when no Greek text is set.
/// </summary>
public class AdminCoursesPageTests : BunitContext
{
    private readonly IAdminCourseConsumer _courseConsumer = Substitute.For<IAdminCourseConsumer>();

    public AdminCoursesPageTests()
    {
        Services.AddSingleton(_courseConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void ReturnsCourse(string? titleEl, string? categoryNameEl)
    {
        var course = new AdminCourseDto(Guid.NewGuid(), "Strength 101", titleEl, null, null, true,
            DateTimeOffset.UtcNow, null, 0, 0, 0, SubscriptionTier.Free,
            CategoryId: Guid.NewGuid(), CategoryNameEn: "Strength", CategoryNameEl: categoryNameEl);
        _courseConsumer
            .GetCoursesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(new PagedResult<AdminCourseDto>([course], 1, 1, 10));
    }

    [Fact]
    public void Greek_ShowsTheGreekTitleAndCategory()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");
        ReturnsCourse("Δύναμη 101", "Δύναμη");

        var row = Render<AdminCourses>().Find("tbody tr").TextContent;

        row.ShouldContain("Δύναμη 101");
        row.ShouldNotContain("Strength");
    }

    [Fact]
    public void GreekWithoutGreekText_FallsBackToEnglish()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");
        ReturnsCourse(null, null);

        var row = Render<AdminCourses>().Find("tbody tr").TextContent;

        row.ShouldContain("Strength 101");
        row.ShouldContain("Strength");
    }
}
