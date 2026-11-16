using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using FitEdu.Application.DTOs;
using FitEdu.Domain.Enums;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

public class AdminAuditIntegrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AdminAuditIntegrationTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task GetAudit_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/api/admin/audit");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAudit_Student_Returns403()
    {
        var student = await _factory.CreateAuthenticatedClientAsync("Student");

        (await student.GetAsync("/api/admin/audit")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAudit_Admin_Returns200WithAPage()
    {
        var admin = await _factory.CreateAuthenticatedClientAsync("Admin");

        var response = await admin.GetAsync("/api/admin/audit");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await response.Content.ReadFromJsonAsync<PagedResult<AdminAuditEntryDto>>();
        page.ShouldNotBeNull();
        page.Page.ShouldBe(1);
    }

    [Fact]
    public async Task AnAdminAction_AppearsInTheLogForThatMember_AndOnlyForThatMember()
    {
        var (admin, adminId) = await _factory.CreateAuthenticatedClientWithIdAsync("Admin");
        var (_, memberId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        var (_, otherMemberId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");

        (await admin.PostAsync($"/api/admin/users/{memberId}/disable", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/admin/users/{memberId}/enable", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.PostAsync($"/api/admin/users/{otherMemberId}/disable", content: null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var page = await admin.GetFromJsonAsync<PagedResult<AdminAuditEntryDto>>($"/api/admin/audit?memberId={memberId}");

        page!.TotalCount.ShouldBe(2);
        page.Items.Select(entry => entry.Action).ShouldBe([AdminAuditAction.UserEnabled, AdminAuditAction.UserDisabled]);
        page.Items.ShouldAllBe(entry => entry.ActorUserId == adminId && entry.TargetUserId == memberId);
        page.Items.ShouldAllBe(entry => entry.ActorEmail != null && entry.TargetEmail != null);
    }

    [Fact]
    public async Task APageSizeOfOne_PagesTheLog()
    {
        var (admin, _) = await _factory.CreateAuthenticatedClientWithIdAsync("Admin");
        var (_, memberId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await admin.PostAsync($"/api/admin/users/{memberId}/disable", content: null);
        await admin.PostAsync($"/api/admin/users/{memberId}/enable", content: null);

        var first = await admin.GetFromJsonAsync<PagedResult<AdminAuditEntryDto>>($"/api/admin/audit?memberId={memberId}&page=1&pageSize=1");
        var second = await admin.GetFromJsonAsync<PagedResult<AdminAuditEntryDto>>($"/api/admin/audit?memberId={memberId}&page=2&pageSize=1");

        first!.Items.ShouldHaveSingleItem().Action.ShouldBe(AdminAuditAction.UserEnabled);
        first.HasNextPage.ShouldBeTrue();
        second!.Items.ShouldHaveSingleItem().Action.ShouldBe(AdminAuditAction.UserDisabled);
        second.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task ADeletedMembersRows_KeepOnlyTheirId()
    {
        var (admin, _) = await _factory.CreateAuthenticatedClientWithIdAsync("Admin");
        var (_, memberId) = await _factory.CreateAuthenticatedClientWithIdAsync("Student");
        await admin.PostAsync($"/api/admin/users/{memberId}/disable", content: null);

        (await admin.DeleteAsync($"/api/admin/users/{memberId}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var page = await admin.GetFromJsonAsync<PagedResult<AdminAuditEntryDto>>($"/api/admin/audit?memberId={memberId}");
        page!.Items.Select(entry => entry.Action).ShouldBe([AdminAuditAction.AccountDeleted, AdminAuditAction.UserDisabled]);
        page.Items.ShouldAllBe(entry => entry.TargetUserId == memberId && entry.TargetEmail == null);
        await _factory.SeedAsync(async db => (await db.Users.AnyAsync(user => user.Id == memberId)).ShouldBeFalse());
    }
}
