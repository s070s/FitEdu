using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using FitEdu.Domain.Entities;
using FitEdu.Domain.Enums;
using FitEdu.Domain.Identity;
using FitEdu.Infrastructure.Data;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace FitEdu.Infrastructure.Tests;

/// <summary>
/// Guards the migration chain the integration suites never exercise (TEST-1): the SQLite-backed
/// integration host builds its schema with <c>EnsureCreated</c>, and the InMemory host runs no
/// migrations at all, so nothing else verifies that the SQL Server migrations are complete and
/// still apply.
///
/// <para><see cref="Model_HasNoPendingChanges_AgainstSqlServer"/> is provider-correct and needs no
/// database — it catches the most common migration bug (a model change with no matching migration).
/// <see cref="FullMigrationChain_AppliesCleanly_AgainstLocalDb"/> actually applies every migration
/// to a throwaway LocalDB database. When LocalDB is unreachable (non-Windows) it returns early, so it
/// runs automatically on developer machines without breaking hosts that lack SQL Server. xUnit 2.x
/// (this solution's version) cannot skip a test at run time (<c>Assert.Skip</c> needs xUnit v3), so
/// such a run is reported as passed: it writes <see cref="NotRunMessage"/> to the test output, and the
/// check that the chain applies is <c>tools/check.ps1 -Migrations</c>, which fails when LocalDB is
/// unreachable.</para>
/// </summary>
public class MigrationChainTests(ITestOutputHelper output)
{
    private const string SqlServerProvider = "Microsoft.EntityFrameworkCore.SqlServer";
    private const string LocalDbServer = "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;";
    private const string NotRunMessage = "LocalDB unreachable: this test did NOT run (xUnit 2.x cannot skip at run time, so it is counted as passed).";

    [Fact]
    public void Model_HasNoPendingChanges_AgainstSqlServer()
    {
        // No connection is opened — HasPendingModelChanges only compares the runtime model to the
        // snapshot, but it must be built under the SQL Server provider so the comparison uses the
        // same type mappings the snapshot was generated with.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(LocalDbServer)
            .Options;
        using var db = new ApplicationDbContext(options);

        db.Database.HasPendingModelChanges().ShouldBeFalse(
            "The EF model has changes with no matching migration — run 'dotnet ef migrations add'.");
    }

    [Fact]
    public async Task FullMigrationChain_AppliesCleanly_AgainstLocalDb()
    {
        if (!await LocalDbIsReachableAsync())
        {
            output.WriteLine(NotRunMessage);
            return;
        }

        var dbName = "FitEduMigrationChainTest_" + Guid.NewGuid().ToString("N");
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer($"{LocalDbServer}Database={dbName};")
            .Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            // Applies every migration in order, creating the database — throws on any malformed
            // migration SQL or ordering problem.
            await db.Database.MigrateAsync();

            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            applied.ShouldNotBeEmpty();
            (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
            output.WriteLine($"Applied {applied.Count} migrations to {dbName}.");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    // The migration before the data migration: the schema is identical, so rows can be added through the model.
    private const string MigrationBeforeDisabledUsers = "20261115210806_RefreshTokenRevokedAtConcurrencyToken";

    private static ApplicationUser NewUser(string name, bool isEnabled, DateTimeOffset? lockoutEnd) => new()
    {
        UserName = $"{name}@test.local",
        Email = $"{name}@test.local",
        FirstName = "First",
        LastName = "Last",
        IsEnabled = isEnabled,
        LockoutEnd = lockoutEnd
    };

    private static ApplicationDbContext NewLocalDbContext(string dbName) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer($"{LocalDbServer}Database={dbName};").Options);

    [Fact]
    public async Task DisabledUsersMigration_DisablesOnlyTheUsersTheOldCodeLockedOutForever_AndDownRestoresThem()
    {
        if (!await LocalDbIsReachableAsync())
        {
            output.WriteLine(NotRunMessage);
            return;
        }

        var dbName = "FitEduDisabledUsersTest_" + Guid.NewGuid().ToString("N");
        await using var db = NewLocalDbContext(dbName);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBeforeDisabledUsers);
            db.Users.AddRange(
                NewUser("old-disabled", isEnabled: true, lockoutEnd: DateTimeOffset.MaxValue),
                NewUser("active", isEnabled: true, lockoutEnd: null),
                NewUser("short-lockout", isEnabled: true, lockoutEnd: DateTimeOffset.UtcNow.AddMinutes(15)),
                NewUser("already-disabled", isEnabled: false, lockoutEnd: null));
            await db.SaveChangesAsync();

            await migrator.MigrateAsync();

            await using (var after = NewLocalDbContext(dbName))
            {
                var enabledAfterUp = await after.Users.ToDictionaryAsync(u => u.UserName!, u => u.IsEnabled);
                enabledAfterUp["old-disabled@test.local"].ShouldBeFalse();
                enabledAfterUp["active@test.local"].ShouldBeTrue();
                enabledAfterUp["short-lockout@test.local"].ShouldBeTrue();
                enabledAfterUp["already-disabled@test.local"].ShouldBeFalse();
                // The lock-out date stays, so the old marker is still readable.
                (await after.Users.SingleAsync(u => u.UserName == "old-disabled@test.local")).LockoutEnd.ShouldBe(DateTimeOffset.MaxValue);
            }

            await migrator.MigrateAsync(MigrationBeforeDisabledUsers);

            await using var afterDown = NewLocalDbContext(dbName);
            (await afterDown.Users.SingleAsync(u => u.UserName == "old-disabled@test.local")).IsEnabled.ShouldBeTrue();
            (await afterDown.Users.SingleAsync(u => u.UserName == "already-disabled@test.local")).IsEnabled.ShouldBeFalse();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    // The migration before the one that lets a billing record outlive its user.
    private const string MigrationBeforeBillingKeepsRecords = "20261116061133_AddAdminAuditEntries";

    [Fact]
    public async Task BillingMigration_KeepsTheRecordOfADeletedAccount_AndDownRefusesWhileSuchRecordsExist()
    {
        if (!await LocalDbIsReachableAsync())
        {
            output.WriteLine(NotRunMessage);
            return;
        }

        var dbName = "FitEduBillingKeepTest_" + Guid.NewGuid().ToString("N");
        await using var db = NewLocalDbContext(dbName);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBeforeBillingKeepsRecords);
            // The schema is the same as the next one apart from the nullability of UserId, so rows go in through the model.
            var member = NewUser("billed", isEnabled: true, lockoutEnd: null);
            var plan = new SubscriptionPlan { Id = Guid.NewGuid(), Name = "Pro", Tier = SubscriptionTier.Pro };
            var transaction = new BillingTransaction
            {
                Id = Guid.NewGuid(),
                UserId = member.Id,
                SubscriptionPlanId = plan.Id,
                Amount = 19.90m,
                Type = BillingTransactionType.Purchase,
                Description = "Purchased Pro"
            };
            db.Users.Add(member);
            db.SubscriptionPlans.Add(plan);
            db.BillingTransactions.Add(transaction);
            await db.SaveChangesAsync();

            await migrator.MigrateAsync();

            await using (var afterUp = NewLocalDbContext(dbName))
            {
                (await afterUp.BillingTransactions.SingleAsync()).UserId.ShouldBe(member.Id);
                afterUp.Users.Remove(await afterUp.Users.SingleAsync());
                await afterUp.SaveChangesAsync();
            }

            await using var afterDelete = NewLocalDbContext(dbName);
            (await afterDelete.BillingTransactions.SingleAsync()).UserId.ShouldBeNull();

            var refusal = await Should.ThrowAsync<SqlException>(() => migrator.MigrateAsync(MigrationBeforeBillingKeepsRecords));
            refusal.Message.ShouldContain("Cannot roll back");
            (await afterDelete.BillingTransactions.CountAsync()).ShouldBe(1);

            await afterDelete.BillingTransactions.ExecuteDeleteAsync();
            await migrator.MigrateAsync(MigrationBeforeBillingKeepsRecords);
            (await afterDelete.Database.GetAppliedMigrationsAsync()).ShouldNotContain(name => name.EndsWith("BillingTransactionKeepsRecordAfterUserDelete"));
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> LocalDbIsReachableAsync()
    {
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer($"{LocalDbServer}Database=master;Connect Timeout=5;")
                .Options;
            await using var probe = new ApplicationDbContext(options);
            return probe.Database.ProviderName == SqlServerProvider
                && await probe.Database.CanConnectAsync();
        }
        catch
        {
            return false;
        }
    }
}
