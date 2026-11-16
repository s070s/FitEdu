using System.Globalization;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Pages;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The admin audit page: a read-only, accessible table of what admins did to accounts.
/// </summary>
public class AdminAuditPageTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly IAdminAuditConsumer _audit = Substitute.For<IAdminAuditConsumer>();

    public AdminAuditPageTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Services.AddSingleton(_audit);
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization().SetAuthorized("admin@fitedu.local").SetRoles("Admin");
    }

    private static AdminAuditEntryDto Entry(
        AdminAuditAction action, string? detail = null, string? targetEmail = "alice@fitedu.local", string targetUserId = "member-1", int minutesAfterNoon = 0) =>
        new(Guid.NewGuid(), "admin-1", "admin@fitedu.local", targetUserId, targetEmail, action, detail, Noon.AddMinutes(minutesAfterNoon));

    private void ReturnsPage(int page, int pageSize, int totalCount, params AdminAuditEntryDto[] entries) =>
        _audit.GetEntriesAsync(page, pageSize).Returns(new PagedResult<AdminAuditEntryDto>(entries.ToList(), totalCount, page, pageSize));

    [Fact]
    public void Render_ListsEachEntryWithWhenAdminActionUserAndDetail()
    {
        ReturnsPage(1, 10, 2,
            Entry(AdminAuditAction.RoleAdded, detail: "Admin"),
            Entry(AdminAuditAction.UserDisabled, minutesAfterNoon: -5));

        var page = Render<AdminAudit>();

        var rows = page.FindAll("tbody tr");
        rows.Count.ShouldBe(2);
        var cells = rows[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        cells.ShouldBe([
            SiteClock.ToSiteTime(Noon).ToString("g", CultureInfo.CurrentCulture),
            "admin@fitedu.local",
            AdminRes.AuditActionRoleAdded,
            "alice@fitedu.local",
            RoleLabels.Label("Admin")]);
        rows[1].QuerySelectorAll("td")[2].TextContent.Trim().ShouldBe(AdminRes.AuditActionUserDisabled);
        rows[1].QuerySelectorAll("td")[4].TextContent.Trim().ShouldBe("—", "an action with no detail shows a dash");
    }

    [Fact]
    public void Render_TheTimeCellCarriesTheMachineReadableMoment()
    {
        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.UserEnabled));

        var page = Render<AdminAudit>();

        page.Find("tbody td time").GetAttribute("datetime").ShouldBe("2026-11-16T12:00:00.0000000+00:00");
    }

    [Fact]
    public void Render_ADeletedAccountShowsOnlyItsId()
    {
        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.AccountDeleted, targetEmail: null, targetUserId: "gone-member-id"));

        var page = Render<AdminAudit>();

        var userCell = page.Find("tbody tr").QuerySelectorAll("td")[3];
        userCell.TextContent.Trim().ShouldBe(ResourceFormat.Fill(AdminRes.AuditDeletedAccountFormat, "gone-member-id"));
    }

    [Fact]
    public void Render_TheTableHasACaptionAndColumnHeaders()
    {
        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.UserEnabled));

        var page = Render<AdminAudit>();

        page.Find("table caption").TextContent.Trim().ShouldBe(AdminRes.AuditLogTitle);
        var headers = page.FindAll("thead th");
        headers.Select(header => header.TextContent.Trim()).ShouldBe(
            [AdminRes.AuditColWhen, AdminRes.AuditColAdmin, AdminRes.AuditColAction, AdminRes.AuditColUser, AdminRes.AuditColDetail]);
        headers.ShouldAllBe(header => header.GetAttribute("scope") == "col");
    }

    [Fact]
    public void Render_EveryCellNamesItsColumnForTheStackedCards()
    {
        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.UserEnabled));

        var page = Render<AdminAudit>();

        page.FindAll("tbody td").Select(cell => cell.GetAttribute("data-label")).ShouldBe(
            [AdminRes.AuditColWhen, AdminRes.AuditColAdmin, AdminRes.AuditColAction, AdminRes.AuditColUser, AdminRes.AuditColDetail]);
        page.Find(".table-responsive").ClassList.ShouldContain("table-responsive--stack-xl");
    }

    [Fact]
    public void Render_IsReadOnly_NoButtonInTheTableAndNoFormControl()
    {
        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.UserDisabled));

        var page = Render<AdminAudit>();

        page.FindAll("table button, table input, table select, table textarea, table a").ShouldBeEmpty();
        page.FindAll("button").Select(button => button.TextContent.Trim()).ShouldAllBe(
            text => text.Contains(GlobalRes.PaginationPrev) || text.Contains(GlobalRes.PaginationNext));
    }

    [Theory]
    [InlineData("en", "Admin audit log")]
    [InlineData("el", "Αρχείο ελέγχου διαχειριστών")]
    public void Render_TheHeadingAndActionFollowTheLanguage(string culture, string expectedHeading)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.PasswordSet));

        var page = Render<AdminAudit>();

        page.Find("h1").TextContent.Trim().ShouldBe(expectedHeading);
        page.Find("tbody tr").QuerySelectorAll("td")[2].TextContent.Trim().ShouldBe(AdminRes.AuditActionPasswordSet);
    }

    [Fact]
    public void Render_NoEntries_SaysSo()
    {
        ReturnsPage(1, 10, 0);

        var page = Render<AdminAudit>();

        page.FindAll("table").ShouldBeEmpty();
        page.Markup.ShouldContain(AdminRes.AuditNoEntries);
    }

    [Fact]
    public void Render_AFailedLoad_ShowsAnAlertWithTryAgainThatLoadsAgain()
    {
        _audit.GetEntriesAsync(1, 10).Returns((PagedResult<AdminAuditEntryDto>?)null);
        var page = Render<AdminAudit>();

        page.Find("[role=alert]").TextContent.ShouldContain(ErrorMessagesRes.UnexpectedErrorTryAgain);

        ReturnsPage(1, 10, 1, Entry(AdminAuditAction.UserEnabled));
        page.Find("[role=alert] button").Click();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Count.ShouldBe(1));
        page.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void NextPage_LoadsTheNextPageOfTheLog()
    {
        ReturnsPage(1, 10, 11, Enumerable.Range(0, 10).Select(minute => Entry(AdminAuditAction.UserEnabled, minutesAfterNoon: -minute)).ToArray());
        ReturnsPage(2, 10, 11, Entry(AdminAuditAction.UserDisabled, targetUserId: "member-11"));
        var page = Render<AdminAudit>();

        page.FindAll("button").Single(button => button.TextContent.Contains(GlobalRes.PaginationNext)).Click();

        page.WaitForAssertion(() => page.FindAll("tbody tr").Count.ShouldBe(1));
        page.Find("tbody tr").QuerySelectorAll("td")[2].TextContent.Trim().ShouldBe(AdminRes.AuditActionUserDisabled);
    }

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}
