using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using FitEdu.Web.Shared.Components.Data;

namespace FitEdu.Web.Tests;

/// <summary>
/// ScrollableTable stacks its rows into cards on phones for every table. A table with many columns
/// opts in to stay stacked until the desktop width (StackBelow="xl"); all other tables are unchanged.
/// </summary>
public class ScrollableTableTests : BunitContext
{
    public ScrollableTableTests() =>
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it into every component

    private IRenderedComponent<ScrollableTable<string>> RenderTable(string? stackBelow = null) =>
        Render<ScrollableTable<string>>((ComponentParameterCollectionBuilder<ScrollableTable<string>> parameters) =>
        {
            parameters
                .Add(component => component.Items, ["one", "two"])
                .Add(component => component.HeaderRow, "<tr><th>Name</th></tr>")
                .Add(component => component.RowTemplate, item => $"<tr><td data-label=\"Name\">{item}</td></tr>");
            if (stackBelow is not null)
            {
                parameters.Add(component => component.StackBelow, stackBelow);
            }
        });

    [Fact]
    public void WithoutAStackBreakpoint_TheWrapperKeepsExactlyItsOwnClasses()
    {
        var wrapper = RenderTable().Find("div");

        wrapper.GetAttribute("class").ShouldBe("table-responsive scrollable-table");
    }

    [Fact]
    public void StackBelowExtraLarge_AddsTheModifierClassToTheWrapper()
    {
        var wrapper = RenderTable(stackBelow: "xl").Find("div");

        wrapper.GetAttribute("class").ShouldBe("table-responsive scrollable-table table-responsive--stack-xl");
    }

    [Fact]
    public void AnUnknownBreakpoint_IsRejectedInsteadOfSilentlyDoingNothing()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RenderTable(stackBelow: "huge"));
    }
}
