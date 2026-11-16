using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FitEdu.TestSupport;

/// <summary>
/// Simulates a store failure: while <see cref="FailSaves"/> is true every save throws, so a test can seed data
/// first, switch it on, and check what a failed save leaves behind in the change tracker.
/// </summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public bool FailSaves { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (FailSaves)
            throw new DbUpdateException("Simulated store failure.");
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
