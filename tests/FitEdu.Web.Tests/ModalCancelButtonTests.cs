using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Pages;
using FitEdu.Web.Shared.Components.Data;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// A modal's Cancel uses the app's ghost button, like every other secondary action, instead of
/// Bootstrap's grey btn-secondary, which reads as a disabled control on the dark theme.
/// </summary>
public class ModalCancelButtonTests : BunitContext
{
    public ModalCancelButtonTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The modals call JS on first render; loose mode answers with defaults.
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static void ShouldBeGhostButton(AngleSharp.Dom.IElement button)
    {
        button.ClassList.ShouldContain("btn-cta");
        button.ClassList.ShouldContain("btn-cta--ghost");
        button.ClassList.ShouldNotContain("btn-secondary");
    }

    [Fact]
    public void ConfirmModal_CancelIsAGhostButton()
    {
        var modal = Render<ConfirmModal>(parameters => parameters
            .Add(component => component.IsVisible, true)
            .Add(component => component.CancelLabel, "Cancel"));

        var cancel = modal.FindAll(".modal-footer button").First(button => button.TextContent.Trim() == "Cancel");

        ShouldBeGhostButton(cancel);
    }

    [Fact]
    public void CategoryFormModal_CancelIsAGhostButton()
    {
        var categoryConsumer = Substitute.For<IAdminCategoryConsumer>();
        categoryConsumer
            .GetCategoriesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminCategoryDto>([], 0, 1, 10));
        Services.AddSingleton(categoryConsumer);
        var page = Render<AdminCategories>();

        page.FindAll("button").First(button => button.TextContent.Trim() == CategoryRes.AddCategory).Click();
        var cancel = page.FindAll(".modal-footer button").First(button => button.TextContent.Trim() == AdminRes.Cancel);

        ShouldBeGhostButton(cancel);
    }
}
