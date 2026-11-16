using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Common;
using FitEdu.Application.Data;

namespace FitEdu.Web.Services;

/// <summary>
/// Once a day deletes the billing records that are past their retention: five full years after the end of
/// the financial year they were made in (<see cref="BillingRetention"/>). The records outlive the accounts
/// they belong to (deleting an account only detaches them), so this job is the only thing that deletes them.
/// A run starts at app start and then every 24 hours; each run reads the clock again, so a host that was off
/// for days catches up on its next run.
/// </summary>
public sealed partial class BillingRetentionPurgeService : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    // Records are deleted in tracked batches, so one run never holds every expired record in memory.
    internal const int BatchSize = 500;

    private readonly IServiceProvider _services;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BillingRetentionPurgeService> _logger;

    public BillingRetentionPurgeService(IServiceProvider services, TimeProvider timeProvider, ILogger<BillingRetentionPurgeService> logger)
    {
        _services = services;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(RunInterval, _timeProvider);
        try
        {
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown (Ctrl+C) — exit quietly
        }
    }

    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        try
        {
            await PurgeExpiredRecordsAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPurgeFailed(_logger, ex);
        }
    }

    /// <summary>Deletes every record created before the cutoff; returns how many.</summary>
    internal async Task<int> PurgeExpiredRecordsAsync(CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var cutoff = BillingRetention.CutoffUtc(_timeProvider.GetUtcNow()).UtcDateTime;

        // Tracked deletes + SaveChanges (not ExecuteDeleteAsync) so this runs on every provider this app
        // targets, including EF Core InMemory (used by the Web.Tests factory).
        var purged = 0;
        while (true)
        {
            var batch = await db.BillingTransactions
                .Where(record => record.CreatedAt < cutoff)
                .OrderBy(record => record.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(stoppingToken);

            if (batch.Count == 0)
                break;

            db.BillingTransactions.RemoveRange(batch);
            await db.SaveChangesAsync(stoppingToken);
            purged += batch.Count;
        }

        if (purged > 0)
            LogRecordsPurged(_logger, purged);

        return purged;
    }

    // Source-generated log methods (LoggerMessage) avoid the CA1848/CA1873 analyzer warnings.
    [LoggerMessage(Level = LogLevel.Error, Message = "BillingRetentionPurgeService: Error during purge.")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "BillingRetentionPurgeService: Deleted {Count} billing record(s) past their retention.")]
    private static partial void LogRecordsPurged(ILogger logger, int count);
}
