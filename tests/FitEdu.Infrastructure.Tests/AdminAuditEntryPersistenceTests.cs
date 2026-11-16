using Microsoft.EntityFrameworkCore;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.TestSupport;
using Shouldly;
using Xunit;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// The audit log runs on SQLite with its foreign keys enforced, so a row that referenced the member
/// (a restrict or cascade) would show up here when the member is deleted.
/// </summary>
public class AdminAuditEntryPersistenceTests
{
    private static ApplicationUser NewUser(string id) => new()
    {
        Id = id,
        UserName = $"{id}@test.local",
        Email = $"{id}@test.local",
        FirstName = "First",
        LastName = "Last"
    };

    [Fact]
    public async Task AnEntry_IsStoredAndReadBackWithEveryField()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var occurredAt = new DateTimeOffset(2026, 11, 16, 8, 30, 0, TimeSpan.Zero);
        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorUserId = "admin-1",
            TargetUserId = "member-1",
            Action = AdminAuditAction.RoleAdded,
            Detail = "Coach",
            OccurredAt = occurredAt
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.AdminAuditEntries.SingleAsync();

        stored.ActorUserId.ShouldBe("admin-1");
        stored.TargetUserId.ShouldBe("member-1");
        stored.Action.ShouldBe(AdminAuditAction.RoleAdded);
        stored.Detail.ShouldBe("Coach");
        stored.OccurredAt.ShouldBe(occurredAt);
    }

    [Fact]
    public async Task AnEntryAboutAMember_SurvivesTheMembersDeletion_AndDoesNotBlockIt()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var member = NewUser("member-1");
        db.Users.Add(member);
        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorUserId = "admin-1",
            TargetUserId = member.Id,
            Action = AdminAuditAction.UserDisabled,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        db.Users.Remove(member);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.Users.AnyAsync(user => user.Id == "member-1")).ShouldBeFalse();
        (await db.AdminAuditEntries.SingleAsync()).TargetUserId.ShouldBe("member-1");
    }

    [Fact]
    public async Task AnEntryByAnAdmin_SurvivesTheAdminsDeletion()
    {
        await using var db = DbContextFactory.CreateSqlite();
        var admin = NewUser("admin-1");
        db.Users.Add(admin);
        db.AdminAuditEntries.Add(new AdminAuditEntry
        {
            ActorUserId = admin.Id,
            TargetUserId = "member-1",
            Action = AdminAuditAction.UserEnabled,
            OccurredAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        db.Users.Remove(admin);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.AdminAuditEntries.SingleAsync()).ActorUserId.ShouldBe("admin-1");
    }
}
