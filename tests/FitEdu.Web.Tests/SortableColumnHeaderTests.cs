using Bunit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Web.Shared.Components.Data;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>The sort indicator is a Bootstrap Icons glyph (no text arrows) that matches aria-sort.</summary>
public class SortableColumnHeaderTests : BunitContext
{
    private const string ColumnKey = "name";

    public SortableColumnHeaderTests()
    {
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
    }

    [Theory]
    [InlineData(ColumnKey, "asc", "bi-caret-up-fill", "ascending")]
    [InlineData(ColumnKey, "desc", "bi-caret-down-fill", "descending")]
    [InlineData("created", "asc", "bi-arrow-down-up", "none")]
    public void Indicator_IsAnIconThatMatchesTheSortState(string currentSortBy, string currentSortDirection, string expectedIcon, string expectedAriaSort)
    {
        var header = Render<SortableColumnHeader>(parameters => parameters
            .Add(component => component.ColumnKey, ColumnKey)
            .Add(component => component.Label, "Name")
            .Add(component => component.CurrentSortBy, currentSortBy)
            .Add(component => component.CurrentSortDir, currentSortDirection));

        var indicator = header.Find(".sort-arrow");
        indicator.ClassList.ShouldContain("bi");
        indicator.ClassList.ShouldContain(expectedIcon);
        indicator.GetAttribute("aria-hidden").ShouldBe("true");
        indicator.TextContent.Trim().ShouldBeEmpty();
        header.Find("[aria-sort]").GetAttribute("aria-sort").ShouldBe(expectedAriaSort);
    }
}
