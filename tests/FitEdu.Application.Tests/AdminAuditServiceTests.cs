using NSubstitute;
using FitEdu.Application.ApiServices;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Application.Tests;

/// <summary>Runs on SQLite, so the newest-first order is the one the database gives (ISO-8601 dates).</summary>
public class AdminAuditServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static AdminAuditService NewService(ApplicationDbContext db)
    {
        var userManager = IdentityMocks.MockUserManager();
        userManager.Users.Returns(db.Users);
        return new AdminAuditService(db, userManager);
    }

    private static ApplicationUser NewUser(string id) => new()
    {
        Id = id,
        UserName = $"{id}@test.local",
        Email = $"{id}@test.local",
        FirstName = "First",
        LastName = "Last"
    };

    private static AdminAuditEntry Entry(string targetUserId, int minutesAfterNoon, AdminAuditAction action = AdminAuditAction.UserDisabled, string actorUserId = "admin-1", string? detail = null) => new()
    {
        ActorUserId = actorUserId,
        TargetUserId = targetUserId,
        Action = action,
        Detail = detail,
        OccurredAt = Noon.AddMinutes(minutesAfterNoon)
    };

    [Fact]
    public async Task GetEntries_ListsTheNewestFirst()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.AdminAuditEntries.AddRange(Entry("member-1", 0), Entry("member-2", 20), Entry("member-3", 10));
        await db.SaveChangesAsync();

        var page = await NewService(db).GetEntriesAsync(1, 10, null);

        page.TotalCount.ShouldBe(3);
        page.Items.Select(item => item.TargetUserId).ShouldBe(["member-2", "member-3", "member-1"]);
    }

    [Fact]
    public async Task GetEntries_PagesTheList_AndReportsTheTotal()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.AdminAuditEntries.AddRange(Enumerable.Range(0, 5).Select(minute => Entry($"member-{minute}", minute)));
        await db.SaveChangesAsync();
        var service = NewService(db);

        var second = await service.GetEntriesAsync(2, 2, null);
        var third = await service.GetEntriesAsync(3, 2, null);

        second.Items.Select(item => item.TargetUserId).ShouldBe(["member-2", "member-1"]);
        second.TotalCount.ShouldBe(5);
        second.TotalPages.ShouldBe(3);
        second.HasNextPage.ShouldBeTrue();
        third.Items.Select(item => item.TargetUserId).ShouldBe(["member-0"]);
        third.HasNextPage.ShouldBeFalse();
    }

    [Fact]
    public async Task GetEntries_WithAMemberId_ListsOnlyTheActionsDoneToThatMember()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.AdminAuditEntries.AddRange(
            Entry("member-1", 0), Entry("member-2", 5), Entry("member-1", 10, AdminAuditAction.UserEnabled),
            // An admin who is the actor of a row is not a member the log was filtered to.
            Entry("member-3", 15, actorUserId: "member-1"));
        await db.SaveChangesAsync();

        var page = await NewService(db).GetEntriesAsync(1, 10, "member-1");

        page.TotalCount.ShouldBe(2);
        page.Items.Select(item => item.Action).ShouldBe([AdminAuditAction.UserEnabled, AdminAuditAction.UserDisabled]);
    }

    [Fact]
    public async Task GetEntries_ABlankMemberId_ListsEveryRow()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.AdminAuditEntries.AddRange(Entry("member-1", 0), Entry("member-2", 5));
        await db.SaveChangesAsync();

        (await NewService(db).GetEntriesAsync(1, 10, "  ")).TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetEntries_ShowsTheEmailOfAnAccountThatStillExists_AndNoneForADeletedOne()
    {
        await using var db = DbContextFactory.CreateSqlite();
        db.Users.AddRange(NewUser("admin-1"), NewUser("member-1"));
        db.AdminAuditEntries.AddRange(Entry("member-1", 0), Entry("deleted-member", 5, AdminAuditAction.AccountDeleted));
        await db.SaveChangesAsync();

        var page = await NewService(db).GetEntriesAsync(1, 10, null);

        var deleted = page.Items.Single(item => item.TargetUserId == "deleted-member");
        deleted.TargetEmail.ShouldBeNull();
        deleted.ActorEmail.ShouldBe("admin-1@test.local");
        var existing = page.Items.Single(item => item.TargetUserId == "member-1");
        existing.TargetEmail.ShouldBe("member-1@test.local");
    }

    [Fact]
    public async Task GetEntries_CarriesTheRowFieldsUnchanged()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var row = Entry("member-1", 7, AdminAuditAction.RoleAdded, detail: "Admin");
        db.AdminAuditEntries.Add(row);
        await db.SaveChangesAsync();

        var item = (await NewService(db).GetEntriesAsync(1, 10, null)).Items.ShouldHaveSingleItem();

        item.Id.ShouldBe(row.Id);
        item.ActorUserId.ShouldBe("admin-1");
        item.Action.ShouldBe(AdminAuditAction.RoleAdded);
        item.Detail.ShouldBe("Admin");
        item.OccurredAt.ShouldBe(Noon.AddMinutes(7));
    }

    [Fact]
    public async Task GetEntries_EmptyLog_ReturnsAnEmptyPage()
    {
        await using var db = DbContextFactory.CreateSqlite();

        var page = await NewService(db).GetEntriesAsync(1, 10, null);

        page.Items.ShouldBeEmpty();
        page.TotalCount.ShouldBe(0);
    }
}
