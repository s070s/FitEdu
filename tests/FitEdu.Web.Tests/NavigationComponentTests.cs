using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.DTOs;
using FitEdu.Shared.Resources;
using FitEdu.Web.Consumers;
using FitEdu.Web.Layout;
using FitEdu.Web.Shared.Components.Assistant;
using FitEdu.Web.Shared.Components.Call;
using FitEdu.Web.Shared.Components.Layout;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The site navigation: MainLayout mounts the phone tab bar once, each role gets its own tabs and
/// header links from the single NavItems source, the menu panel lists every link of the role, and
/// the search box folds into an icon button on phones.
/// </summary>
public class NavigationComponentTests : BunitContext
{
    private const string GuestRole = "";

    private readonly BunitAuthorizationContext _authorization;

    public NavigationComponentTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        _authorization = AddAuthorization();
    }

    private void SignInAs(string role)
    {
        if (role == GuestRole)
        {
            return;
        }

        _authorization.SetAuthorized("member@fitedu.local");
        _authorization.SetRoles(role);
    }

    // Nullable to match what GetAttribute returns, so the lists compare without a cast.
    private static List<string?> Hrefs(IEnumerable<NavItem> items) => items.Select(item => (string?)item.Href).ToList();

    [Fact]
    public void MainLayout_RendersTheBottomNavOnce()
    {
        SignInAs(NavItems.StudentRole);
        // The other layout parts need many services and are covered elsewhere.
        ComponentFactories.AddStub<ImpersonationBanner>();
        ComponentFactories.AddStub<NavMenu>();
        ComponentFactories.AddStub<SiteFooter>();
        ComponentFactories.AddStub<CallOverlayHost>();
        ComponentFactories.AddStub<AssistantWidget>();
        RenderFragment page = builder => builder.AddMarkupContent(0, "<p class=\"page-body\">Page</p>");

        var layout = Render<MainLayout>(parameters => parameters.Add(component => component.Body, page));

        layout.FindComponents<BottomNav>().Count.ShouldBe(1);
        layout.FindAll("nav.bottom-nav").Count.ShouldBe(1);
        layout.Find("main#main-content .page-body").TextContent.ShouldBe("Page");
    }

    [Theory]
    [InlineData(GuestRole, new[] { "", "exercises", "pricing", "login" })]
    [InlineData(NavItems.StudentRole, new[] { "", "courses", "exercises", "paths", "profile" })]
    [InlineData(NavItems.AdminRole, new[] { "admin/users", "admin/courses", "admin/assessments", "admin/analytics", "admin/blog" })]
    public void BottomNav_ShowsTheTabsOfTheRole(string role, string[] expectedHrefs)
    {
        SignInAs(role);

        var bottomNav = Render<BottomNav>();

        var hrefs = bottomNav.FindAll(".bottom-nav a").Select(link => link.GetAttribute("href")).ToList();
        hrefs.ShouldBe(expectedHrefs);
    }

    [Fact]
    public void BottomNav_EveryTabHasAnIconAVisibleLabelAndAnAccessibleName()
    {
        SignInAs(NavItems.StudentRole);

        var bottomNav = Render<BottomNav>();

        bottomNav.Find("nav.bottom-nav").GetAttribute("aria-label").ShouldBe(NavMenuRes.QuickNavigation);
        foreach (var tab in bottomNav.FindAll(".bottom-nav a"))
        {
            tab.QuerySelector(".bi").ShouldNotBeNull();
            var visibleLabel = tab.QuerySelector(".bottom-nav__label")!.TextContent;
            visibleLabel.ShouldNotBeNullOrWhiteSpace();
            // WCAG 2.5.3: the accessible name contains the visible label.
            tab.GetAttribute("aria-label")!.ShouldContain(visibleLabel, Case.Insensitive);
        }
    }

    [Fact]
    public void BottomNav_SignedInWithoutAKnownRole_ShowsNoTabs()
    {
        _authorization.SetAuthorized("someone@fitedu.local");

        var bottomNav = Render<BottomNav>();

        bottomNav.FindAll("nav.bottom-nav").ShouldBeEmpty();
        bottomNav.FindAll(".bottom-nav-spacer").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(GuestRole)]
    [InlineData(NavItems.StudentRole)]
    [InlineData(NavItems.AdminRole)]
    public void NavMenu_HeaderShowsOnlyTheInlineLinksOfTheRole(string role)
    {
        SignInAs(role);
        var expectedItems = role switch
        {
            NavItems.StudentRole => NavItems.Student,
            NavItems.AdminRole => NavItems.Admin,
            _ => NavItems.Guest
        };

        var navMenu = Render<NavMenu>();

        var hrefs = navMenu.FindAll(".site-links a").Select(link => link.GetAttribute("href")).ToList();
        hrefs.ShouldBe(Hrefs(expectedItems.Where(item => item.InHeader)));
        navMenu.FindAll(".site-menu").ShouldBeEmpty();
    }

    [Fact]
    public void NavMenu_MenuButton_OpensAPanelWithEveryLinkOfTheRole()
    {
        SignInAs(NavItems.StudentRole);
        var navMenu = Render<NavMenu>();

        navMenu.Find(".site-menu-toggle").Click();

        navMenu.Find(".site-menu-toggle").GetAttribute("aria-expanded").ShouldBe("true");
        var hrefs = navMenu.FindAll(".site-menu a").Select(link => link.GetAttribute("href")).ToList();
        hrefs.ShouldBe(Hrefs(NavItems.Student));
        navMenu.Find(".site-menu .culture-selector").ShouldNotBeNull();
    }

    [Fact]
    public void NavMenu_EscapeOrNavigation_ClosesTheMenu()
    {
        SignInAs(NavItems.StudentRole);
        var navMenu = Render<NavMenu>();
        navMenu.Find(".site-menu-toggle").Click();

        navMenu.Find("#site-menu").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        navMenu.FindAll(".site-menu").ShouldBeEmpty();

        // Right after a click, focus is still on the menu button.
        navMenu.Find(".site-menu-toggle").Click();
        navMenu.Find(".site-menu-toggle").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        navMenu.FindAll(".site-menu").ShouldBeEmpty();

        navMenu.Find(".site-menu-toggle").Click();
        Services.GetRequiredService<NavigationManager>().NavigateTo("/courses");
        navMenu.WaitForAssertion(() => navMenu.FindAll(".site-menu").ShouldBeEmpty());
    }

    [Fact]
    public void NavMenu_Guest_MenuButtonIsForCompactScreensOnly()
    {
        // Every guest link fits inline on wide screens, so the button hides there; signed-in
        // members keep it because most of their links live only in the panel.
        var guestMenu = Render<NavMenu>();
        guestMenu.Find(".site-menu-toggle").ClassList.ShouldContain("site-menu-toggle--compact-only");
        guestMenu.Find(".site-link--cta").GetAttribute("href").ShouldBe("register");
    }

    [Fact]
    public void NavMenu_Student_KeepsTheMenuButtonOnWideScreens()
    {
        SignInAs(NavItems.StudentRole);

        var navMenu = Render<NavMenu>();

        navMenu.Find(".site-menu-toggle").ClassList.ShouldNotContain("site-menu-toggle--compact-only");
    }

    [Fact]
    public void NavItems_EachRoleHasAtMostFiveTabs()
    {
        NavItems.Guest.Count(item => item.InBottomNav).ShouldBe(4);
        NavItems.Student.Count(item => item.InBottomNav).ShouldBe(5);
        NavItems.Admin.Count(item => item.InBottomNav).ShouldBe(5);
    }

    [Fact]
    public void SiteSearchBox_ToggleButton_OpensAndClosesThePhonePanel()
    {
        var searchConsumer = Substitute.For<ISearchConsumer>();
        searchConsumer.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>())
            .Returns(new SiteSearchResultDto([], SemanticSearchUsed: true));
        Services.AddSingleton(searchConsumer);
        var searchBox = Render<SiteSearchBox>();
        searchBox.FindAll(".site-search--expanded").ShouldBeEmpty();

        searchBox.Find(".site-search-toggle").Click();

        searchBox.Find(".site-search").ClassList.ShouldContain("site-search--expanded");
        searchBox.Find(".site-search-toggle").GetAttribute("aria-expanded").ShouldBe("true");

        searchBox.Find(".site-search-input").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        searchBox.FindAll(".site-search--expanded").ShouldBeEmpty();
    }
}
