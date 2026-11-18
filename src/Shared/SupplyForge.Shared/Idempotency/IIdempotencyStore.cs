namespace SupplyForge.Shared.Idempotency;

/// <summary>
/// MOD-1a: IdempotencyKey is a shared table owned by Platform Core infrastructure.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Try to claim a key for this payload hash. Returns false if the key
    /// already exists with a different payload hash (mismatch) or is in progress.
    /// </summary>
    Task<IdempotencyClaimResult> TryClaimAsync(
        string key,
        string payloadHash,
        CancellationToken cancellationToken = default);

    Task CompleteAsync(string key, string? responseSnapshotJson, CancellationToken cancellationToken = default);

    Task FailAsync(string key, CancellationToken cancellationToken = default);
}

public enum IdempotencyClaimStatus
{
    Claimed,
    AlreadyCompleted,
    InProgress,
    PayloadMismatch,
}

public sealed record IdempotencyClaimResult(IdempotencyClaimStatus Status);
