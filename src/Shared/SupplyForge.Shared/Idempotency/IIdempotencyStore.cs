namespace SupplyForge.Shared.Idempotency;

/// <summary>
/// MOD-1a: IdempotencyKey is a shared table owned by Platform Core infrastructure.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Try to claim a key for this payload hash.
    /// Returns <see cref="IdempotencyClaimStatus.Claimed"/> if the key was created for this request.
    /// Returns <see cref="IdempotencyClaimStatus.AlreadyCompleted"/> if the key exists with "Completed" status and same payload hash.
    /// Returns <see cref="IdempotencyClaimStatus.PayloadMismatch"/> if the key exists with a different payload hash.
    /// Returns <see cref="IdempotencyClaimStatus.InProgress"/> if the key exists with a non-completed status (concurrent request).
    /// </summary>
    /// <remarks>
    /// Under the ambient transaction model (MOD-TXN-01, ADR-003), a failed operation rolls back the idempotency claim
    /// automatically with the transaction. No explicit FailAsync is needed — retry with the same key will find no claim
    /// and claim it anew. The "Failed" status is obsolete and not used.
    /// </remarks>
    Task<IdempotencyClaimResult> TryClaimAsync(
        string key,
        string payloadHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark the idempotency key as completed with the response snapshot.
    /// Called after the business operation succeeds.
    /// </summary>
    Task CompleteAsync(string key, string? responseSnapshotJson, CancellationToken cancellationToken = default);

    /// <summary>
    /// Release a claimed idempotency key so it can be claimed again.
    /// Used when the business operation fails after claiming but before completing.
    /// </summary>
    Task ReleaseAsync(string key, CancellationToken cancellationToken = default);
}

public enum IdempotencyClaimStatus
{
    Claimed,
    AlreadyCompleted,
    InProgress,
    PayloadMismatch,
}

public sealed record IdempotencyClaimResult(IdempotencyClaimStatus Status);
