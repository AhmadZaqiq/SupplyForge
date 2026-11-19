using SupplyForge.PlatformCore.Application.Email;
using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Notifications;

namespace SupplyForge.PlatformCore.Infrastructure.Application.Notifications;

/// <summary>
/// ADR-008 delivery state machine for the email side-channel (PLT-051,
/// PLT-052, TD-2, INT-003). One attempt = atomic claim → adapter call →
/// recorded transition on the already-committed row. Neither caller holds an
/// open session around the adapter call (MOD-TXN-02) — the inline raise
/// attempt and the background retry worker both invoke this from outside any
/// transaction, and adapter failures are recorded state, never exceptions
/// surfaced to the raise endpoint (PLT-052: 201 with the row pending, not
/// 500).
///
/// <para>Explicit transitions (every write is a single conditional UPDATE —
/// duplicate concurrent processors and late results can never corrupt the
/// row):</para>
/// <list type="bullet">
/// <item><c>Pending(n)</c> --claim CAS(n)--> <c>Pending(n+1, lease)</c>
/// (exactly one winner; losers never touch the adapter);</item>
/// <item>adapter OK --> <c>Sent</c> (+SentAt);</item>
/// <item>adapter fail, n+1 &lt; MaxAttempts --> <c>Pending</c> with
/// <c>NextAttemptAt = now + backoff(n+1)</c> (INT-003);</item>
/// <item>adapter fail, n+1 &gt;= MaxAttempts --> <c>Failed</c> dead-letter
/// (NextAttemptAt NULL, visible in the read API);</item>
/// <item><c>Pending/Failed</c> --TD-2 manual mark-sent--> <c>Sent</c>
/// (audited); <c>Sent</c> is idempotent; a late adapter outcome for a row
/// that is no longer Pending is discarded.</item>
/// </list>
/// Attempt count semantics: <c>DeliveryAttempts</c> counts attempts
/// *started* (claimed), so a crash between claim and result costs one
/// attempt and the lease expiry re-schedules the row — at-least-once across
/// crashes, exactly-once per sweep under concurrency.
/// </summary>
public sealed class NotificationDeliveryService
{
    /// <summary>A0 dev mailbox (ADR-008: no real recipient in A0).</summary>
    private const string DevMailbox = "dev@supplyforge.local";

    /// <summary>INT-003 jitter: up to +20% on top of the backoff delay.</summary>
    private const double JitterFraction = 0.2;

    private readonly INotificationStore _store;
    private readonly IEmailSender _email;
    private readonly EmailOptions _options;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public NotificationDeliveryService(
        INotificationStore store,
        IEmailSender email,
        EmailOptions options,
        IAuditWriter audit,
        TimeProvider clock)
    {
        _store = store;
        _email = email;
        _options = options;
        _audit = audit;
        _clock = clock;
    }

    /// <summary>
    /// One delivery attempt for a committed row. <c>Email:Mode=Disabled</c>
    /// returns before any claim (PLT-051: row untouched, visible pending,
    /// never scheduled). Otherwise: claim the attempt atomically (skip if a
    /// concurrent processor already owns it), call the adapter under a
    /// bounded <c>Email:SendTimeout</c> (INT-004), then record the outcome.
    /// Success records Sent; failure records Pending with exponential backoff
    /// + jitter or Failed at <c>Email:MaxAttempts</c> (dead-letter, visible —
    /// PLT-052). <paramref name="attemptsSoFar"/> is the count the caller
    /// read (0 for the inline first attempt) and becomes the claim's
    /// compare-and-swap expectation. Host-shutdown cancellation propagates
    /// without recording (the lease re-schedules the row); a timeout is
    /// recorded like any other failure.
    /// </summary>
    public async Task TryDeliverAsync(
        Guid notificationId,
        string notificationType,
        string title,
        string body,
        int attemptsSoFar,
        CancellationToken cancellationToken = default)
    {
        if (!_options.AttemptsDelivery)
        {
            // PLT-051: pending-only mode — never touch the adapter, never
            // claim, so the row stays visible pending for manual mark-sent
            // (TD-2) and no retry can ever be scheduled.
            return;
        }

        // Claim before the adapter (duplicate-processing guard): one CAS
        // increments the attempt count and sets the in-flight lease, so a
        // concurrent sweeper can neither select nor claim the row while this
        // attempt runs. Losers return without sending anything.
        var claimed = await _store.TryClaimDeliveryAttemptAsync(
            notificationId,
            attemptsSoFar,
            leaseUntil: _clock.GetUtcNow() + InFlightLease(),
            cancellationToken);
        if (!claimed)
        {
            return;
        }

        var attempts = attemptsSoFar + 1;

        // Adapter call, bounded (INT-004): a hung send can never stall the
        // retry worker's batch. The linked token cancels only this attempt —
        // host-shutdown cancellation still propagates (the claim is kept and
        // lease expiry re-schedules the row: at-least-once across
        // shutdowns); a timeout is returned as an ordinary failure message.
        string? failure;
        try
        {
            failure = await SendOnceAsync(notificationType, title, body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failure = ex.Message;
        }

        // Recording happens outside the adapter try: a record error is a
        // storage error and propagates (per-row logged by the worker), never
        // mislabelled as an adapter failure.
        if (failure is null)
        {
            await _store.RecordDeliveryAttemptAsync(
                notificationId,
                succeeded: true,
                attempts,
                error: null,
                nextAttemptAt: null,
                cancellationToken);
        }
        else
        {
            await RecordFailureAsync(notificationId, attempts, failure, cancellationToken);
        }
    }

    /// <summary>
    /// One bounded adapter call: <c>null</c> = accepted, a message = failed
    /// (including the send timeout). Host-shutdown cancellation propagates
    /// as <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<string?> SendOnceAsync(
        string notificationType,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        using var attemptTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptTimeout.CancelAfter(_options.SendTimeout);
        try
        {
            await _email.SendAsync(
                DevMailbox,
                $"[SupplyForge] {notificationType}",
                $"{title}\n\n{body}",
                attemptTimeout.Token);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The adapter exceeded Email:SendTimeout — recorded like any
            // other failure so the row retries or dead-letters instead of
            // silently hanging in flight.
            return $"Email send timed out after {_options.SendTimeout}.";
        }
    }

    private async Task RecordFailureAsync(
        Guid notificationId,
        int attempts,
        string error,
        CancellationToken cancellationToken)
    {
        var nextAttemptAt = attempts >= _options.MaxAttempts
            ? null // INT-003: max attempts → Failed (dead-letter).
            : (DateTimeOffset?)(_clock.GetUtcNow()
                + ComputeBackoff(attempts, _options.RetryBaseDelay, _options.RetryMaxDelay));

        await _store.RecordDeliveryAttemptAsync(
            notificationId,
            succeeded: false,
            attempts,
            error,
            nextAttemptAt,
            cancellationToken);
    }

    /// <summary>
    /// In-flight lease on a claimed attempt: long enough to cover the
    /// bounded adapter call twice, so an aborted process's row becomes
    /// sweepable again only after the attempt it started is certainly over.
    /// </summary>
    private TimeSpan InFlightLease() =>
        TimeSpan.FromTicks(_options.SendTimeout.Ticks * 2);

    /// <summary>
    /// TD-2 manual mark-sent (audited): Pending/Failed → Sent, already-Sent
    /// is an idempotent no-op; <see cref="NotificationMarkSentResult.NotFound"/>
    /// lets the endpoint answer 404.
    /// </summary>
    public async Task<NotificationMarkSentResult> MarkSentAsync(
        Guid notificationId,
        string? actorUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await _store.MarkSentManuallyAsync(notificationId, cancellationToken);
        if (result == NotificationMarkSentResult.MarkedSent)
        {
            await _audit.WriteAsync(
                "user",
                actorUserId,
                "notification.mark_sent",
                "Notification",
                notificationId.ToString(),
                branchId: null,
                detailsJson: null,
                cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// INT-003 exponential backoff + jitter: <c>base · 2^(n−1)</c> capped at
    /// <paramref name="maxDelay"/>, plus up to +20% random jitter so retries
    /// from a failure burst never align. Public static for direct unit
    /// coverage of the curve (ADR-008: exact knobs are recommendations).
    /// </summary>
    public static TimeSpan ComputeBackoff(int attempts, TimeSpan baseDelay, TimeSpan maxDelay)
    {
        var exponent = Math.Min(Math.Max(attempts, 1) - 1, 30);
        var raw = baseDelay.Ticks * Math.Pow(2, exponent);
        var capped = Math.Min(raw, maxDelay.Ticks);
        var jitter = capped * JitterFraction * Random.Shared.NextDouble();
        return TimeSpan.FromTicks((long)(capped + jitter));
    }
}
