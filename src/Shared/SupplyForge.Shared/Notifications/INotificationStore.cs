namespace SupplyForge.Shared.Notifications;

/// <summary>
/// MOD-1a: Notification is a shared table owned by Platform Core infrastructure (ADR-008).
/// </summary>
public interface INotificationStore
{
    Task<Guid> RaiseAsync(
        string notificationType,
        string? userId,
        string? branchId,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken = default);

    Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-008 in-app read API (A0): notifications visible to a user —
    /// broadcast rows (<c>UserId</c> null) plus rows addressed to them —
    /// newest first and bounded by <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<NotificationItem>> ListVisibleToUserAsync(
        Guid userId, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// PLT-052 duplicate-processing guard: atomically claim the right to run
    /// one delivery attempt. Succeeds (returns <c>true</c>) only when the row
    /// is still <c>Pending</c> at <paramref name="expectedAttempts"/> — a
    /// single conditional UPDATE increments <c>DeliveryAttempts</c> and sets
    /// <c>NextAttemptAt</c> to <paramref name="leaseUntil"/> (the in-flight
    /// lease, which hides the row from the sweep while the adapter runs).
    /// Exactly one of any number of concurrent claimers wins; losers must
    /// not call the adapter. The claimed attempt is counted whether or not
    /// the process later records an outcome (crash → lease expiry re-schedules
    /// the row; delivery is at-least-once across crashes).
    /// </summary>
    Task<bool> TryClaimDeliveryAttemptAsync(
        Guid notificationId,
        int expectedAttempts,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ADR-008 / PLT-051 / PLT-052: record the outcome of a claimed adapter
    /// attempt. Conditional on the row still being <c>Pending</c> at
    /// <paramref name="attempts"/> (the value the claim produced): a
    /// concurrent manual mark-sent or any other transition wins and the
    /// outcome is silently discarded — a late result can never overwrite a
    /// terminal state. Success → Sent; failure → Pending with
    /// <paramref name="nextAttemptAt"/> scheduled (INT-003 backoff) or Failed
    /// when <c>null</c> (max attempts reached — dead-letter, visible).
    /// Zero rows updated = the row no longer exists in that state (deleted or
    /// concurrently transitioned); not an error.
    /// </summary>
    Task RecordDeliveryAttemptAsync(
        Guid notificationId,
        bool succeeded,
        int attempts,
        string? error,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PLT-052 / INT-003: due retry work for the delivery worker — Pending
    /// rows whose <c>NextAttemptAt</c> has passed, soonest first, bounded by
    /// <paramref name="limit"/> (never an unbounded scan).
    /// </summary>
    Task<IReadOnlyList<NotificationDeliveryItem>> ListDueForDeliveryAsync(
        int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// TD-2 manual mark-sent: Pending or Failed → Sent (the operator records
    /// delivery that happened outside the adapter) as one conditional UPDATE
    /// — concurrent callers resolve to exactly one <see cref="NotificationMarkSentResult.MarkedSent"/>;
    /// already-Sent is a no-op.
    /// <see cref="NotificationMarkSentResult.NotFound"/> when no row exists.
    /// </summary>
    Task<NotificationMarkSentResult> MarkSentManuallyAsync(
        Guid notificationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-model row for the in-app notification list (ADR-008 read API).
/// Delivery fields expose PLT-051 pending / PLT-052 failure state.
/// </summary>
public sealed record NotificationItem(
    Guid Id,
    string NotificationType,
    string Title,
    string Body,
    string? Link,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt,
    string DeliveryStatus,
    int DeliveryAttempts,
    DateTimeOffset? SentAt,
    string? LastDeliveryError);

/// <summary>Work item for the delivery retry sweep (PLT-052).</summary>
public sealed record NotificationDeliveryItem(
    Guid Id,
    string NotificationType,
    string Title,
    string Body,
    int DeliveryAttempts);

/// <summary>Outcome of a TD-2 manual mark-sent request.</summary>
public enum NotificationMarkSentResult
{
    /// <summary>No such notification — the endpoint answers 404.</summary>
    NotFound,

    /// <summary>Already Sent — idempotent no-op, answers 200.</summary>
    AlreadySent,

    /// <summary>Transitioned to Sent — audited, answers 200.</summary>
    MarkedSent,
}
