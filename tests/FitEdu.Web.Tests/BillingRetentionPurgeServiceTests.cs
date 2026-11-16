using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FitEdu.Application.Data;
using FitEdu.Domain.Entities;
using FitEdu.Infrastructure.Data;
using FitEdu.TestSupport;
using FitEdu.Web.Services;
using Shouldly;
using Xunit;

namespace FitEdu.Web.Tests;

/// <summary>
/// The purge runs on a fake clock and names every moment in UTC; the cutoff counts the financial year in
/// Europe/Athens (<c>BillingRetention</c>), so the tests do not depend on the zone of the machine.
/// </summary>
public class BillingRetentionPurgeServiceTests
{
    // During 2026 the cutoff is 2021-01-01 00:00 in Athens: 2020-12-31T22:00Z.
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 0, 0, TimeSpan.Zero);

    private static BillingTransaction Transaction(DateTime createdAtUtc, string? userId = null) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        SubscriptionPlanId = Guid.NewGuid(),
        Amount = 10m,
        Description = "Purchased Plus",
        CreatedAt = createdAtUtc
    };

    private static BillingRetentionPurgeService Build(ApplicationDbContext db, DateTimeOffset now, RecordingLogger<BillingRetentionPurgeService>? logger = null)
    {
        var services = new ServiceCollection();
        // Singleton (not Scoped): the service creates and disposes its own scope, and a scoped registration of the
        // shared `db` would be disposed with it, which would break the assertions made against `db` afterwards.
        services.AddSingleton<IApplicationDbContext>(_ => db);

        return new BillingRetentionPurgeService(services.BuildServiceProvider(), new FixedTimeProvider(now), logger ?? new RecordingLogger<BillingRetentionPurgeService>());
    }

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    [Fact]
    public async Task Purge_DeletesRecordsBeforeTheCutoff_AndKeepsTheRestIncludingTheCutoffMoment()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var oneSecondBefore = Transaction(Utc(2020, 12, 31, 21, 59, 59));
        var atTheCutoff = Transaction(Utc(2020, 12, 31, 22, 0, 0));
        var recent = Transaction(Utc(2026, 11, 1));
        db.BillingTransactions.AddRange(oneSecondBefore, atTheCutoff, recent);
        await db.SaveChangesAsync();

        var purged = await Build(db, Now).PurgeExpiredRecordsAsync(CancellationToken.None);

        purged.ShouldBe(1);
        var kept = await db.BillingTransactions.Select(t => t.Id).ToListAsync();
        kept.ShouldBe([atTheCutoff.Id, recent.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task Purge_DeletesAnOldRecordWhetherOrNotItsAccountStillExists()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.BillingTransactions.AddRange(
            Transaction(Utc(2019, 5, 1), userId: null),
            Transaction(Utc(2019, 6, 1), userId: "still-a-member"));
        await db.SaveChangesAsync();

        var purged = await Build(db, Now).PurgeExpiredRecordsAsync(CancellationToken.None);

        purged.ShouldBe(2);
        (await db.BillingTransactions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Purge_FollowsTheClock_AroundAthensNewYear()
    {
        // A record of 1 June 2020 belongs to financial year 2020. It is kept through all of 2025 (Athens time)
        // and past its five full years only when 2026 begins in Athens: 22:00 UTC on 31 December 2025.
        await using var db = DbContextFactory.CreateInMemory();
        db.BillingTransactions.Add(Transaction(Utc(2020, 6, 1)));
        await db.SaveChangesAsync();

        var oneMinuteBeforeNewYear = new DateTimeOffset(2025, 12, 31, 21, 59, 0, TimeSpan.Zero);
        (await Build(db, oneMinuteBeforeNewYear).PurgeExpiredRecordsAsync(CancellationToken.None)).ShouldBe(0);
        (await db.BillingTransactions.CountAsync()).ShouldBe(1);

        var atNewYear = new DateTimeOffset(2025, 12, 31, 22, 0, 0, TimeSpan.Zero);
        (await Build(db, atNewYear).PurgeExpiredRecordsAsync(CancellationToken.None)).ShouldBe(1);
        (await db.BillingTransactions.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Purge_MoreRecordsThanOneBatch_DeletesAllOfThemAndLogsTheCount()
    {
        await using var db = DbContextFactory.CreateInMemory();
        var oldRecordCount = (BillingRetentionPurgeService.BatchSize * 2) + 1;
        for (var index = 0; index < oldRecordCount; index++)
            db.BillingTransactions.Add(Transaction(Utc(2018, 1, 1).AddMinutes(index)));
        var recent = Transaction(Utc(2026, 1, 1));
        db.BillingTransactions.Add(recent);
        await db.SaveChangesAsync();
        var logger = new RecordingLogger<BillingRetentionPurgeService>();

        var purged = await Build(db, Now, logger).PurgeExpiredRecordsAsync(CancellationToken.None);

        purged.ShouldBe(oldRecordCount);
        (await db.BillingTransactions.SingleAsync()).Id.ShouldBe(recent.Id);
        logger.Information.ShouldHaveSingleItem().ShouldContain(oldRecordCount.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Purge_NothingExpired_DeletesNothingAndLogsNothing()
    {
        await using var db = DbContextFactory.CreateInMemory();
        db.BillingTransactions.Add(Transaction(Utc(2026, 1, 1)));
        await db.SaveChangesAsync();
        var logger = new RecordingLogger<BillingRetentionPurgeService>();

        var purged = await Build(db, Now, logger).PurgeExpiredRecordsAsync(CancellationToken.None);

        purged.ShouldBe(0);
        (await db.BillingTransactions.CountAsync()).ShouldBe(1);
        logger.Information.ShouldBeEmpty();
    }
}
