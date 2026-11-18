using InspectionReview.Web.Auth;
using InspectionReview.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace InspectionReview.Web.Services;

/// <summary>Who holds a finding's edit lock. DisplayName is null if it was released while we looked.</summary>
public sealed record LockHolder(string? DisplayName, DateTime LockedAt)
{
    public DateTime ExpiresAt => LockedAt + EditLockService.Timeout;
}

public sealed record LockResult(bool Acquired, LockHolder? HeldBy)
{
    public static readonly LockResult Success = new(true, null);
}

/// <summary>
/// One inspector at a time edits a finding. Each method is a single conditional UPDATE, so two
/// inspectors racing for the lock can't both win. These updates don't touch Version: taking or
/// refreshing the lock must not make the holder's own copy stale.
/// </summary>
public sealed class EditLockService(
    IDbContextFactory<AppDbContext> dbFactory,
    AccessPolicy accessPolicy,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    /// <summary>A lock not refreshed for this long counts as free.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(30);

    /// <summary>How long before expiry the holder is warned.</summary>
    public static readonly TimeSpan WarningBefore = TimeSpan.FromMinutes(5);

    /// <summary>Takes the lock if it's free, expired, or already ours.</summary>
    public async Task<LockResult> TryAcquireAsync(int findingId, CancellationToken cancellationToken = default)
    {
        if (!await accessPolicy.CouldEditFindingAsync(findingId))
        {
            throw new UnauthorizedAccessException("You can't edit this finding.");
        }

        var userId = await CurrentUserIdAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;
        var expired = now - Timeout;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var updated = await db.Findings
            .Where(f => f.Id == findingId && (f.LockedById == null || f.LockedAt < expired || f.LockedById == userId))
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.LockedById, userId)
                .SetProperty(f => f.LockedAt, now), cancellationToken);

        if (updated == 1)
        {
            return LockResult.Success;
        }

        var holder = await db.Findings
            .Where(f => f.Id == findingId && f.LockedAt != null)
            .Select(f => new LockHolder(f.LockedBy!.DisplayName, f.LockedAt!.Value))
            .SingleOrDefaultAsync(cancellationToken);
        return new LockResult(false, holder);
    }

    /// <summary>Restarts the timeout. False if someone else has the lock now.</summary>
    public async Task<bool> RefreshAsync(int findingId, CancellationToken cancellationToken = default)
    {
        var userId = await CurrentUserIdAsync(cancellationToken);
        var now = clock.GetUtcNow().UtcDateTime;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Findings
            .Where(f => f.Id == findingId && f.LockedById == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(f => f.LockedAt, now), cancellationToken) == 1;
    }

    /// <summary>Does nothing if the current user doesn't hold the lock, so it's safe to call on page close.</summary>
    public async Task ReleaseAsync(int findingId, CancellationToken cancellationToken = default)
    {
        if ((await currentUser.GetAsync(cancellationToken))?.UserId is not string userId)
        {
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Findings
            .Where(f => f.Id == findingId && f.LockedById == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(f => f.LockedById, (string?)null)
                .SetProperty(f => f.LockedAt, (DateTime?)null), cancellationToken);
    }

    private async Task<string> CurrentUserIdAsync(CancellationToken cancellationToken) =>
        (await currentUser.GetAsync(cancellationToken))?.UserId
        ?? throw new UnauthorizedAccessException("You must be signed in.");
}
