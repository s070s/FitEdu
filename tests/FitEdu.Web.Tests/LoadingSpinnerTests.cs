using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Shared.Resources;
using FitEdu.Web.Shared.Components.Layout;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>The pulse loader keeps the old LoadingSpinner API: size, optional label, overlay.</summary>
public class LoadingSpinnerTests : BunitContext
{
    public LoadingSpinnerTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
    }

    [Fact]
    public void Default_IsAMediumPulseWithTheLoadingName()
    {
        var loader = Render<LoadingSpinner>();

        var status = loader.Find("[role=status]");
        status.ClassList.ShouldContain("pulse-loader");
        status.ClassList.ShouldContain("pulse-loader--medium");
        status.GetAttribute("aria-label").ShouldBe(GlobalRes.Loading);
        loader.Find(".pulse-loader__line").GetAttribute("pathLength").ShouldBe("100");
        loader.FindAll(".pulse-loader__label").ShouldBeEmpty();
    }

    [Fact]
    public void LargeOverlayWithLabel_AppliesEveryOption()
    {
        var loader = Render<LoadingSpinner>(parameters => parameters
            .Add(component => component.Size, LoadingSpinner.SpinnerSize.Large)
            .Add(component => component.Overlay, true)
            .Add(component => component.Label, "Loading courses"));

        var status = loader.Find("[role=status]");
        status.ClassList.ShouldContain("pulse-loader--large");
        status.ClassList.ShouldContain("pulse-loader--overlay");
        status.GetAttribute("aria-label").ShouldBe("Loading courses");
        loader.Find(".pulse-loader__label").TextContent.ShouldBe("Loading courses");
    }
}
