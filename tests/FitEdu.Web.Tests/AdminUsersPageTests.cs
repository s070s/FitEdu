using System.Globalization;
using System.Net;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using FitEdu.Application.ApiInterfaces;
using FitEdu.Application.Common;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using FitEdu.Shared.Resources;
using FitEdu.Shared.Resources.Messages;
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
    private readonly BunitPersistentComponentState _persistentState;

    public AdminUsersPageTests()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        CultureInfo.CurrentUICulture = new CultureInfo("en");
        Services.AddSingleton(_users);
        Services.AddSingleton(Substitute.For<IAuthService>());
        Services.AddSingleton(new PresenceService(Substitute.For<ICallService>()));
        Services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build()); // _Imports.razor injects it everywhere
        _persistentState = AddBunitPersistentComponentState();
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

    [Theory]
    [InlineData("en")]
    [InlineData("el")]
    public void RowButtons_EveryButtonNamesItsUser_SoAScreenReaderCanTellTheRowsApart(string culture)
    {
        CultureInfo.CurrentUICulture = new CultureInfo(culture);
        var page = RenderThreeUsers();
        var rows = page.FindAll("tbody tr");
        var emails = new[] { MemberEmail, DisabledMemberEmail };

        var accessibleNames = new List<string>();
        for (var rowIndex = 0; rowIndex < emails.Length; rowIndex++)
        {
            foreach (var button in rows[rowIndex].QuerySelectorAll("td.table-actions button"))
            {
                var accessibleName = button.GetAttribute("aria-label");
                accessibleName.ShouldNotBeNull();
                accessibleName.ShouldContain(emails[rowIndex]);
                // WCAG 2.5.3: the accessible name starts with the text a sighted user reads on the button.
                accessibleName.ShouldStartWith(button.TextContent.Trim());
                accessibleNames.Add(accessibleName);
            }
        }

        accessibleNames.Count.ShouldBe(8);
        accessibleNames.Distinct().Count().ShouldBe(accessibleNames.Count, "two buttons share an accessible name");
    }

    // ---- Loading the list ------------------------------------------------------------------------

    private static PagedResult<AdminUserDto> OnePage(string email) =>
        new([User("only-" + email, email, true, "Student")], 1, 1, 10);

    [Fact]
    public async Task List_AnOlderSlowAnswerArrivingAfterANewerOne_DoesNotReplaceTheNewerList()
    {
        const string olderEmail = "older.answer@fitedu.local";
        const string newerEmail = "newer.answer@fitedu.local";
        ReturnsUsers(User("member-1", MemberEmail, true, "Student"));
        var page = Render<AdminUsers>();
        var olderAnswer = new TaskCompletionSource<PagedResult<AdminUserDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newerAnswer = new TaskCompletionSource<PagedResult<AdminUserDto>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _users.GetUsersAsync(1, 10, Arg.Any<string?>(), "firstname", "asc").Returns(olderAnswer.Task);
        _users.GetUsersAsync(1, 10, Arg.Any<string?>(), "firstname", "desc").Returns(newerAnswer.Task);

        // Two clicks on the name header: ascending first, then descending, each asking the API again.
        var olderRequest = page.FindAll("thead th")[1].ClickAsync(new MouseEventArgs());
        var newerRequest = page.FindAll("thead th")[1].ClickAsync(new MouseEventArgs());
        newerAnswer.SetResult(OnePage(newerEmail));
        await newerRequest;
        olderAnswer.SetResult(OnePage(olderEmail));
        await olderRequest;

        page.Find("tbody").TextContent.ShouldContain(newerEmail);
        page.Find("tbody").TextContent.ShouldNotContain(olderEmail);
    }

    [Fact]
    public void List_TheApiAnswersWithAnError_OffersToTryAgainInsteadOfASkeletonThatNeverEnds()
    {
        // The consumer turns every failed request (a 500, a 403, no connection) into a null list.
        _users.GetUsersAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns((PagedResult<AdminUserDto>?)null);

        var page = Render<AdminUsers>();

        page.FindAll(".skeleton-block").ShouldBeEmpty();
        page.Find(".alert-danger").TextContent.ShouldContain(ErrorMessagesRes.UnexpectedErrorTryAgain);
        page.Find("button.btn-cta").TextContent.Trim().ShouldBe(GlobalRes.TryAgain);
    }

    [Fact]
    public void List_ThePrerenderedFirstPageIsReusedInteractively_WithoutAskingTheApiAgain()
    {
        _persistentState.Persist("admin-users-first-page", OnePage(MemberEmail));

        var page = Render<AdminUsers>();

        page.Find("tbody").TextContent.ShouldContain(MemberEmail);
        _users.DidNotReceiveWithAnyArgs().GetUsersAsync();
    }

    [Fact]
    public void List_TheFirstPageIsHandedToTheInteractiveRender()
    {
        ReturnsUsers(User("member-1", MemberEmail, true, "Student"));
        Render<AdminUsers>();

        _persistentState.TriggerOnPersisting();

        _persistentState.TryTake<PagedResult<AdminUserDto>>("admin-users-first-page", out var handedOver).ShouldBeTrue();
        handedOver!.Items.Single().Email.ShouldBe(MemberEmail);
    }

    [Fact]
    public void List_AFailedLoad_IsNotHandedToTheInteractiveRender()
    {
        // A stored failure would show an empty table without calling the API once the circuit starts.
        _users.GetUsersAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(Task.FromException<PagedResult<AdminUserDto>?>(new HttpRequestException("down")));
        Render<AdminUsers>();

        _persistentState.TriggerOnPersisting();

        _persistentState.TryTake<PagedResult<AdminUserDto>>("admin-users-first-page", out _).ShouldBeFalse();
    }

    // ---- Delete ----------------------------------------------------------------------------------

    private IRenderedComponent<AdminUsers> OpenDeleteConfirmation()
    {
        var page = RenderThreeUsers();
        ClickRowButton(page.FindAll("tbody tr")[0], AdminRes.Delete);
        return page;
    }

    private static void ConfirmDelete(IRenderedComponent<AdminUsers> page) =>
        page.FindAll(".modal-footer button")[^1].Click();

    [Fact]
    public void DeleteConfirmation_NamesTheUserBeingDeleted()
    {
        var page = OpenDeleteConfirmation();

        page.Find(".modal-body").TextContent.ShouldBe(ResourceFormat.Fill(AdminRes.ConfirmDeleteUser, MemberEmail));
        page.Find(".modal-body").TextContent.ShouldContain(MemberEmail);
    }

    [Fact]
    public void Delete_Accepted_ReloadsTheListAndSaysSo()
    {
        _users.DeleteUserAsync("member-1").Returns(ApiActionResult.Success);
        var page = OpenDeleteConfirmation();

        ConfirmDelete(page);

        _users.Received(1).DeleteUserAsync("member-1");
        page.Find(".alert-success").TextContent.ShouldContain(AdminRes.UserDeleted);
        page.FindAll(".modal-dialog").ShouldBeEmpty();
    }

    [Fact]
    public void Delete_TheApiAnswersConflict_SaysOtherDataStillDependsOnTheUser()
    {
        _users.DeleteUserAsync("member-1").Returns(ApiActionResult.Refused(HttpStatusCode.Conflict, "User could not be deleted."));
        var page = OpenDeleteConfirmation();

        ConfirmDelete(page);

        page.Find(".alert-danger").TextContent.ShouldContain(AdminRes.UserDeleteConflict);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(null)]
    public void Delete_AnyOtherFailure_AsksToTryAgain(HttpStatusCode? status)
    {
        _users.DeleteUserAsync("member-1")
            .Returns(status is null ? ApiActionResult.Unreachable : ApiActionResult.Refused(status.Value));
        var page = OpenDeleteConfirmation();

        ConfirmDelete(page);

        page.Find(".alert-danger").TextContent.ShouldContain(AdminRes.UserDeleteFailed);
    }

    [Fact]
    public void Delete_TheOnlyRowOfTheLastPage_StepsBackToThePreviousPage()
    {
        // Eleven users at ten per page: page 2 holds one row. Deleting it leaves ten, so page 2 would be empty.
        var pageOneUsers = Enumerable.Range(1, 10).Select(number => User($"member-{number}", $"member{number}@fitedu.local", true, "Student")).ToList();
        _users.GetUsersAsync(1, 10, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminUserDto>(pageOneUsers, 11, 1, 10));
        _users.GetUsersAsync(2, 10, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminUserDto>([User("member-11", "member11@fitedu.local", true, "Student")], 11, 2, 10));
        _users.DeleteUserAsync("member-11").Returns(ApiActionResult.Success);
        var page = Render<AdminUsers>();
        page.FindAll(".pagination-controls button")[1].Click();
        ClickRowButton(page.Find("tbody tr"), AdminRes.Delete);
        _users.ClearReceivedCalls();

        ConfirmDelete(page);

        _users.Received(1).GetUsersAsync(1, 10, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>());
        _users.DidNotReceive().GetUsersAsync(2, Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public void Delete_OneOfSeveralRowsOnTheLastPage_StaysOnThatPage()
    {
        var pageTwoUsers = new List<AdminUserDto>
        {
            User("member-11", "member11@fitedu.local", true, "Student"),
            User("member-12", "member12@fitedu.local", true, "Student")
        };
        _users.GetUsersAsync(1, 10, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminUserDto>(
                Enumerable.Range(1, 10).Select(number => User($"member-{number}", $"member{number}@fitedu.local", true, "Student")).ToList(), 12, 1, 10));
        _users.GetUsersAsync(2, 10, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(new PagedResult<AdminUserDto>(pageTwoUsers, 12, 2, 10));
        _users.DeleteUserAsync("member-11").Returns(ApiActionResult.Success);
        var page = Render<AdminUsers>();
        page.FindAll(".pagination-controls button")[1].Click();
        ClickRowButton(page.Find("tbody tr"), AdminRes.Delete);
        _users.ClearReceivedCalls();

        ConfirmDelete(page);

        _users.Received(1).GetUsersAsync(2, 10, Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<string>());
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
