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

    // The migration before the one that drops the unused publish, soft-delete and progress columns.
    private const string MigrationBeforeTrimmedColumns = "20261116070636_BillingTransactionKeepsRecordAfterUserDelete";

    [Theory]
    [InlineData("Modules", "IsPublished", "1")]
    [InlineData("Modules", "PublishedAt", "SYSDATETIMEOFFSET()")]
    [InlineData("Lessons", "IsPublished", "1")]
    [InlineData("Categories", "IsPublished", "1")]
    [InlineData("Exercises", "IsDeleted", "1")]
    [InlineData("BlogArticles", "DeletedAt", "SYSDATETIMEOFFSET()")]
    [InlineData("AssessmentDefinitions", "IsDeleted", "1")]
    [InlineData("Enrollments", "ProgressJson", "'progress'")]
    public async Task TrimMigration_RefusesToDropAColumnThatHoldsData_AndDropsItOnceTheDataIsGone(
        string table, string column, string valueSql)
    {
        if (!await LocalDbIsReachableAsync())
        {
            output.WriteLine(NotRunMessage);
            return;
        }

        var dbName = "FitEduTrimGuardTest_" + Guid.NewGuid().ToString("N");
        await using var db = NewLocalDbContext(dbName);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBeforeTrimmedColumns);
            await InsertRowAsync(db, table, new Dictionary<string, string> { [column] = valueSql });

            var refusal = await Should.ThrowAsync<SqlException>(() => migrator.MigrateAsync());
            refusal.Message.ShouldContain($"Cannot drop {table}.");
            (await ColumnExistsAsync(db, table, column)).ShouldBeTrue();

            var deleteRows = $"DELETE FROM [{table}]";
            await db.Database.ExecuteSqlRawAsync(deleteRows);
            await migrator.MigrateAsync();
            (await ColumnExistsAsync(db, table, column)).ShouldBeFalse();
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task TrimMigration_KeepsRowsWithDefaultValues_AndDownAddsTheColumnsBackWithTheirDefaults()
    {
        if (!await LocalDbIsReachableAsync())
        {
            output.WriteLine(NotRunMessage);
            return;
        }

        var dbName = "FitEduTrimDownTest_" + Guid.NewGuid().ToString("N");
        await using var db = NewLocalDbContext(dbName);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBeforeTrimmedColumns);
            await InsertRowAsync(db, "Categories", []);
            (await IndexExistsAsync(db, "IX_LessonCompletions_UserId")).ShouldBeTrue();

            await migrator.MigrateAsync();

            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Categories]").SingleAsync()).ShouldBe(1);
            (await ColumnExistsAsync(db, "Categories", "IsPublished")).ShouldBeFalse();
            (await ColumnExistsAsync(db, "Testimonials", "RowVersion")).ShouldBeTrue();
            (await IndexExistsAsync(db, "IX_LessonCompletions_UserId")).ShouldBeFalse();

            await migrator.MigrateAsync(MigrationBeforeTrimmedColumns);

            (await ColumnExistsAsync(db, "Categories", "IsPublished")).ShouldBeTrue();
            (await ColumnExistsAsync(db, "Enrollments", "ProgressJson")).ShouldBeTrue();
            (await ColumnExistsAsync(db, "Testimonials", "RowVersion")).ShouldBeFalse();
            (await IndexExistsAsync(db, "IX_LessonCompletions_UserId")).ShouldBeTrue();
            (await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM [Categories] WHERE [IsPublished] = 0 AND [PublishedAt] IS NULL").SingleAsync())
                .ShouldBe(1);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    private static async Task<bool> ColumnExistsAsync(ApplicationDbContext db, string table, string column) =>
        await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = {0} AND COLUMN_NAME = {1}", table, column)
            .SingleAsync() > 0;

    private static async Task<bool> IndexExistsAsync(ApplicationDbContext db, string indexName) =>
        await db.Database.SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = {0}", indexName).SingleAsync() > 0;

    /// <summary>
    /// Inserts one row into a table of an older schema with raw SQL, because the current model no longer has every
    /// column. Every required column without a default gets a dummy value of its type; the overrides (column to SQL
    /// literal) replace or add values. Foreign keys are not checked, so the row needs no parents.
    /// </summary>
    private static async Task InsertRowAsync(ApplicationDbContext db, string table, Dictionary<string, string> overrides)
    {
        var values = new Dictionary<string, string>();
        await db.Database.OpenConnectionAsync();
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            command.CommandText =
                "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS " +
                "WHERE TABLE_NAME = @table AND IS_NULLABLE = 'NO' AND COLUMN_DEFAULT IS NULL AND DATA_TYPE <> 'timestamp'";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@table";
            parameter.Value = table;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                values[reader.GetString(0)] = DummyValueSql(reader.GetString(1));
        }

        foreach (var (column, valueSql) in overrides)
            values[column] = valueSql;

        var columns = string.Join(", ", values.Keys.Select(name => $"[{name}]"));
        var insert = $"INSERT INTO [{table}] ({columns}) VALUES ({string.Join(", ", values.Values)})";
        var skipForeignKeyChecks = $"ALTER TABLE [{table}] NOCHECK CONSTRAINT ALL";
        await db.Database.ExecuteSqlRawAsync(skipForeignKeyChecks);
        await db.Database.ExecuteSqlRawAsync(insert);
    }

    private static string DummyValueSql(string sqlType) => sqlType switch
    {
        "nvarchar" or "varchar" or "nchar" or "char" => "'x'",
        "bit" or "int" or "bigint" or "smallint" or "tinyint" or "decimal" or "float" => "0",
        "uniqueidentifier" => "NEWID()",
        "datetimeoffset" => "SYSDATETIMEOFFSET()",
        "datetime2" or "datetime" => "SYSUTCDATETIME()",
        "varbinary" => "0x00",
        _ => throw new InvalidOperationException($"No dummy value for SQL type {sqlType}.")
    };

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
