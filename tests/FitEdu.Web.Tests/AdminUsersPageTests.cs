using System.Globalization;
using System.Net;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.TestSupport;
using FitEdu.Web.Consumers;
using FitEdu.Web.Extensions;
using FitEdu.Web.Interfaces;
using FitEdu.Web.Pages;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The admin user table: what a row shows and offers, plus the set-password dialog, the delete
/// confirmation, and the paging and search behaviour of the list.
/// </summary>
public class AdminUsersPageTests : BunitContext
{
    // The tests switch culture; Dispose puts the runner's cultures back.
    private readonly CultureScope _cultureScope = new();

    private const string MemberEmail = "alice.johnson@fitedu.local";
    private const string DisabledMemberEmail = "ben.carter@fitedu.local";
    private const string AdminEmail = "admin@fitedu.local";

    private readonly IAdminUserConsumer _users = Substitute.For<IAdminUserConsumer>();

    public AdminUsersPageTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Services.AddSingleton(_users);
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(new PresenceService(Substitute.For<ICallService>()));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        AddBunitPersistentComponentState();
        JSInterop.Mode = JSRuntimeMode.Loose;
        AddAuthorization().SetAuthorized(AdminEmail).SetRoles("Admin");
    }

    private static AdminUserDto User(string id, string email, bool isEnabled = true, params string[] roles) =>
        new(id, email, "Test", "Person", null, EmailConfirmed: true, isEnabled, "Beginner",
            roles.ToList(), SubscriptionTier.Plus, new DateTime(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc));

    private void ReturnsUsers(params AdminUserDto[] users) =>
        _users.GetUsersAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminUserDto>(users.ToList(), users.Length, 1, 10));

    private IRenderedComponent<AdminUsers> RenderThreeUsers()
    {
        ReturnsUsers(
            User("member-1", MemberEmail, true, "Student"),
            User("member-2", DisabledMemberEmail, false, "Student"),
            User("admin-1", AdminEmail, true, "Admin"));
        return Render<AdminUsers>();
    }

    private static List<string> ButtonNames(AngleSharp.Dom.IElement row) =>
        row.QuerySelectorAll("td.table-actions button").Select(button => button.TextContent.Trim()).ToList();

    [Fact]
    public void Render_ListsEveryUserWithTierRolesAndTheActionsTheirRoleAllows()
    {
        var page = RenderThreeUsers();

        var rows = page.FindAll("tbody tr");
        rows.Count.ShouldBe(3);
        rows[0].TextContent.ShouldContain(MemberEmail);
        rows[0].TextContent.ShouldContain(TierLabels.Name(SubscriptionTier.Plus));
        rows[0].TextContent.ShouldContain(RoleLabels.Label("Student"));
        ButtonNames(rows[0]).ShouldBe([AdminRes.LoginAs, AdminRes.ResetPassword, AdminRes.DisableUser, AdminRes.Delete]);
        ButtonNames(rows[1]).ShouldBe([AdminRes.LoginAs, AdminRes.ResetPassword, AdminRes.EnableUser, AdminRes.Delete]);
        ButtonNames(rows[2]).ShouldBeEmpty("an admin row offers no action");
    }

    // ---- Set-password dialog ---------------------------------------------------------------------

    private static void ClickRowButton(AngleSharp.Dom.IElement row, string buttonText) =>
        row.QuerySelectorAll("td.table-actions button").Single(button => button.TextContent.Trim() == buttonText).Click();

    private IRenderedComponent<AdminUsers> OpenSetPasswordDialog()
    {
        var page = RenderThreeUsers();
        ClickRowButton(page.FindAll("tbody tr")[0], AdminRes.ResetPassword);
        return page;
    }

    private static void SubmitPassword(IRenderedComponent<AdminUsers> page, string password, string confirmation)
    {
        page.Find("#reset-pwd-new").Change(password);
        page.Find("#reset-pwd-confirm").Change(confirmation);
        page.FindAll(".modal-footer button")[^1].Click();
    }

    [Fact]
    public void SetPasswordDialog_BothFieldsAreNewPasswordFields_SoABrowserDoesNotFillTheAdminsOwnPassword()
    {
        var page = OpenSetPasswordDialog();

        page.Find("#reset-pwd-new").GetAttribute("autocomplete").ShouldBe("new-password");
        page.Find("#reset-pwd-confirm").GetAttribute("autocomplete").ShouldBe("new-password");
    }

    [Fact]
    public void SetPasswordDialog_ATooShortPassword_ShowsTheErrorAsAnAlertWithoutCallingTheApi()
    {
        var page = OpenSetPasswordDialog();

        SubmitPassword(page, "Short1a", "Short1a");

        var alert = page.Find(".modal-body [role='alert']");
        alert.TextContent.Trim().ShouldBe(AdminRes.PasswordMinLength);
        _users.DidNotReceiveWithAnyArgs().SetPasswordAsync(default!, default!);
    }

    [Fact]
    public void SetPasswordDialog_AMinimumLengthPassword_IsSentAndTheDialogCloses()
    {
        _users.SetPasswordAsync("member-1", "Passw0rd").Returns(ApiActionResult.Success);
        var page = OpenSetPasswordDialog();

        SubmitPassword(page, "Passw0rd", "Passw0rd");

        _users.Received(1).SetPasswordAsync("member-1", "Passw0rd");
        page.FindAll(".modal-dialog").ShouldBeEmpty();
        page.Find(".alert-success").TextContent.ShouldContain(AdminRes.PasswordUpdated);
    }

    [Fact]
    public void SetPasswordDialog_TheServerRefusesThePassword_ShowsTheServersRuleMessage()
    {
        const string serverMessage = "Passwords must have at least one uppercase ('A'-'Z').";
        _users.SetPasswordAsync("member-1", "passw0rd").Returns(ApiActionResult.Refused(HttpStatusCode.BadRequest, serverMessage));
        var page = OpenSetPasswordDialog();

        SubmitPassword(page, "passw0rd", "passw0rd");

        page.Find(".modal-body [role='alert']").TextContent.Trim().ShouldBe(serverMessage);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "User not found.")]
    [InlineData(HttpStatusCode.TooManyRequests, null)]
    [InlineData(null, null)]
    public void SetPasswordDialog_AnyOtherFailure_ShowsTheGenericMessage(HttpStatusCode? status, string? detail)
    {
        _users.SetPasswordAsync("member-1", "Passw0rd")
            .Returns(status is null ? ApiActionResult.Unreachable : ApiActionResult.Refused(status.Value, detail));
        var page = OpenSetPasswordDialog();

        SubmitPassword(page, "Passw0rd", "Passw0rd");

        page.Find(".modal-body [role='alert']").TextContent.Trim().ShouldBe(AdminRes.PasswordUpdateFailed);
    }

    protected override void Dispose(bool disposing)
    {
        _cultureScope.Dispose();
        base.Dispose(disposing);
    }
}
