using Microsoft.EntityFrameworkCore;
using FitEdu.Application.Data;

namespace FitEdu.Application.Common;

/// <summary>
/// Revokes every still-active refresh token of a user (after a password change, which already rotated
/// the security stamp). <c>RevokedAt</c> is a concurrency token (REL-8), so a refresh that rotates one of
/// the tokens at the same moment makes the save throw; that must not fail a password change that has
/// already happened, so the tokens that are still active are read and saved again.
/// </summary>
public static class RefreshTokenRevocation
{
    private const int MaxAttempts = 3;

    // Tracked mutation + SaveChanges (not ExecuteUpdateAsync) so this runs on every provider,
    // including the EF InMemory provider integration tests use.
    public static async Task RevokeActiveTokensAsync(
        IApplicationDbContext context, string userId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var activeTokens = await context.RefreshTokens
                .Where(rt => rt.UserId == userId && rt.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var token in activeTokens)
                token.RevokedAt = DateTimeOffset.UtcNow;

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException exception) when (attempt < MaxAttempts)
            {
                // Take the stored values of the rows that changed under us, so the next read sees them as revoked.
                foreach (var entry in exception.Entries)
                    await entry.ReloadAsync(cancellationToken);
            }
        }
    }
}
