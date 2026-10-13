using System.Globalization;
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
/// The category delete confirmation names the category in the admin's language: the Greek name
/// under Greek, falling back to the English name when no Greek name is set.
/// </summary>
public class AdminCategoriesPageTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private readonly IAdminCategoryConsumer _categoryConsumer = Substitute.For<IAdminCategoryConsumer>();

    public AdminCategoriesPageTests()
    {
        Services.AddSingleton(_categoryConsumer);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The modals call JS on first render; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private void ReturnsCategory(string nameEn, string? nameEl)
    {
        var category = new AdminCategoryDto(Guid.NewGuid(), nameEn, nameEl, 3, 1, DateTimeOffset.UtcNow);
        _categoryConsumer
            .GetCategoriesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminCategoryDto>([category], 1, 1, 10));
    }

    private static string OpenDeleteConfirm(IRenderedComponent<AdminCategories> page)
    {
        page.FindAll("button").First(button => button.TextContent.Trim() == AdminRes.Delete).Click();
        return page.Find(".modal-body p").TextContent;
    }

    [Fact]
    public void DeleteConfirm_Greek_NamesTheCategoryByItsGreekName()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");
        ReturnsCategory("Strength", "Δύναμη");

        var message = OpenDeleteConfirm(Render<AdminCategories>());

        message.ShouldContain("«Δύναμη»");
        message.ShouldNotContain("Strength");
    }

    [Fact]
    public void DeleteConfirm_GreekWithoutAGreekName_FallsBackToTheEnglishName()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("el");
        ReturnsCategory("Strength", null);

        var message = OpenDeleteConfirm(Render<AdminCategories>());

        message.ShouldContain("«Strength»");
    }

    [Fact]
    public void DeleteConfirm_English_NamesTheCategoryByItsEnglishName()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        ReturnsCategory("Strength", "Δύναμη");

        var message = OpenDeleteConfirm(Render<AdminCategories>());

        message.ShouldContain("'Strength'");
    }

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}
