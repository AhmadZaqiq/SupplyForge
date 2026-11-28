using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Idempotency;
using SupplyForge.Shared.Jobs;
using SupplyForge.Shared.Notifications;
using SupplyForge.Shared.Sequencing;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.Infrastructure.Services;

/// <summary>
/// MOD-1a implementations for Platform Core–owned shared tables.
/// Other modules depend only on the Shared interfaces, never on this type.
/// </summary>
public sealed class PlatformSharedServices :
    IAuditWriter,
    IIdempotencyStore,
    IDocumentSequenceService,
    INotificationStore,
    IJobRunStore
{
    private readonly PlatformCoreDbContext _db;
    private readonly TimeProvider _clock;

    public PlatformSharedServices(PlatformCoreDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task WriteAsync(
        string actorType,
        string? actorId,
        string action,
        string? entityType,
        string? entityId,
        string? branchId,
        string? detailsJson,
        CancellationToken cancellationToken = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OccurredAt = _clock.GetUtcNow(),
            ActorType = actorType,
            ActorId = actorId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            BranchId = branchId,
            // PLT-044: shared choke point for every module's audit details —
            // configured sensitive fields are redacted before persistence.
            DetailsJson = SensitiveDataRedactor.RedactDetails(detailsJson),
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IdempotencyClaimResult> TryClaimAsync(
        string key,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        var existing = await _db.IdempotencyKeys
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (existing is null)
        {
            var claim = new IdempotencyKey
            {
                Key = key,
                PayloadHash = payloadHash,
                Status = "InProgress",
                CreatedAt = _clock.GetUtcNow(),
            };
            _db.IdempotencyKeys.Add(claim);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return new IdempotencyClaimResult(IdempotencyClaimStatus.Claimed);
            }
            catch (DbUpdateException)
            {
                // IDEM-05: another request claimed this key between our read
                // and our insert (Key is the PK). The violation means the
                // winner's row exists — detach our failed insert so a later
                // SaveChanges on this context cannot retry it, then re-read
                // fresh to report the winner's status instead of surfacing
                // the race as an exception. If no row can be read (the
                // failure was not this race), rethrow the original error.
                _db.Entry(claim).State = EntityState.Detached;
                var winner = await _db.IdempotencyKeys
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
                if (winner is null)
                {
                    throw;
                }

                return EvaluateClaim(winner, payloadHash);
            }
        }

        return EvaluateClaim(existing, payloadHash);
    }

    /// <summary>
    /// Claim evaluation shared by the read path and the lost-race re-read:
    /// payload mismatch wins first, then completed, else the claim is still
    /// in progress (IDEM-05).
    /// The "Failed" status is obsolete — under ambient transactions (MOD-TXN-01, ADR-003),
    /// failed attempts roll back the claim automatically; retry finds no claim and claims anew.
    /// </summary>
    private static IdempotencyClaimResult EvaluateClaim(IdempotencyKey existing, string payloadHash)
    {
        if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
        {
            return new IdempotencyClaimResult(IdempotencyClaimStatus.PayloadMismatch);
        }

        if (existing.Status == "Completed")
        {
            return new IdempotencyClaimResult(IdempotencyClaimStatus.AlreadyCompleted);
        }

        return new IdempotencyClaimResult(IdempotencyClaimStatus.InProgress);
    }

    public async Task CompleteAsync(string key, string? responseSnapshotJson, CancellationToken cancellationToken = default)
    {
        var row = await _db.IdempotencyKeys.FindAsync([key], cancellationToken);
        if (row == null)
        {
            // Key was released (failed operation), nothing to complete
            return;
        }

        row.Status = "Completed";
        row.ResponseSnapshotJson = responseSnapshotJson;
        row.UpdatedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _db.IdempotencyKeys.FindAsync([key], cancellationToken);
        if (row != null)
        {
            _db.IdempotencyKeys.Remove(row);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>PLT-073b: bounded compare-and-swap rounds per allocation.</summary>
    private const int MaxAllocationAttempts = 100;

    public async Task<string> NextAsync(
        string scope,
        string prefix,
        int padWidth,
        CancellationToken cancellationToken = default)
    {
        // PLT-073b: allocation is a compare-and-swap on the value just read
        // (the same pattern as TryClaimDeliveryAttemptAsync), never a
        // read-modify-write — two concurrent callers can never mint the same
        // number. The first caller inserts the row; a lost insert race finds
        // the winner's row and takes the CAS path. Gaps stay allowed
        // (PLT-073c); duplicates do not.
        for (var attempt = 0; attempt < MaxAllocationAttempts; attempt++)
        {
            var current = await _db.DocumentSequences
                .Where(x => x.Scope == scope)
                .Select(x => (long?)x.LastValue)
                .FirstOrDefaultAsync(cancellationToken);

            if (current is null)
            {
                var row = new DocumentSequence
                {
                    Scope = scope,
                    LastValue = 1,
                    UpdatedAt = _clock.GetUtcNow(),
                };
                _db.DocumentSequences.Add(row);
                try
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return $"{prefix}{row.LastValue.ToString().PadLeft(padWidth, '0')}";
                }
                catch (DbUpdateException)
                {
                    // PK race on the first allocation: the row exists now
                    // unless the failure was something else, which must not
                    // be swallowed behind the retry.
                    _db.Entry(row).State = EntityState.Detached;
                    if (!await _db.DocumentSequences.AnyAsync(x => x.Scope == scope, cancellationToken))
                    {
                        throw;
                    }

                    continue;
                }
            }

            var expected = current.Value;
            var claimed = await _db.DocumentSequences
                .Where(x => x.Scope == scope && x.LastValue == expected)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.LastValue, expected + 1)
                    .SetProperty(x => x.UpdatedAt, _clock.GetUtcNow()),
                    cancellationToken);

            if (claimed == 1)
            {
                var next = expected + 1;
                return $"{prefix}{next.ToString().PadLeft(padWidth, '0')}";
            }

            // Another caller advanced the row first — re-read and retry.
        }

        throw new InvalidOperationException(
            $"Could not allocate sequence '{scope}' after {MaxAllocationAttempts} attempts.");
    }

    public async Task<Guid> RaiseAsync(
        string notificationType,
        string? userId,
        string? branchId,
        string title,
        string body,
        string? link,
        CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        _db.Notifications.Add(new Notification
        {
            Id = id,
            NotificationType = notificationType,
            UserId = userId is null ? null : Guid.Parse(userId),
            BranchId = branchId is null ? null : Guid.Parse(branchId),
            Title = title,
            Body = body,
            Link = link,
            CreatedAt = _clock.GetUtcNow(),
        });

        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task MarkReadAsync(Guid notificationId, string userId, CancellationToken cancellationToken = default)
    {
        var row = await _db.Notifications
            .FirstOrDefaultAsync(x => x.Id == notificationId, cancellationToken)
            ?? throw new InvalidOperationException($"Notification {notificationId} not found.");

        if (row.UserId is not null && row.UserId.Value != Guid.Parse(userId))
        {
            throw new InvalidOperationException("Notification does not belong to this user.");
        }

        row.ReadAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationItem>> ListVisibleToUserAsync(
        Guid userId, int limit, CancellationToken cancellationToken = default)
    {
        // ADR-008 read API: a user sees broadcast rows (UserId null) and rows
        // addressed to them; newest first, bounded — never an unbounded scan.
        return await _db.Notifications
            .Where(n => n.UserId == null || n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(limit)
            .Select(n => new NotificationItem(
                n.Id, n.NotificationType, n.Title, n.Body, n.Link, n.CreatedAt, n.ReadAt,
                n.DeliveryStatus, n.DeliveryAttempts, n.SentAt, n.LastDeliveryError))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Matches Notification.LastDeliveryError HasMaxLength(500).</summary>
    private const int MaxDeliveryErrorLength = 500;

    public async Task<bool> TryClaimDeliveryAttemptAsync(
        Guid notificationId,
        int expectedAttempts,
        DateTimeOffset leaseUntil,
        CancellationToken cancellationToken = default)
    {
        // Single conditional UPDATE = compare-and-swap on (Pending,
        // expectedAttempts): exactly one concurrent claimer wins, and the
        // in-flight lease (NextAttemptAt) hides the row from the sweep while
        // the adapter runs — a second sweeper can neither select nor claim
        // it (PLT-052 duplicate-processing guard, boundary: INT-002).
        var claimed = await _db.Notifications
            .Where(n => n.Id == notificationId
                && n.DeliveryStatus == NotificationDeliveryStatus.Pending
                && n.DeliveryAttempts == expectedAttempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DeliveryAttempts, n => n.DeliveryAttempts + 1)
                .SetProperty(n => n.NextAttemptAt, leaseUntil),
                cancellationToken);
        return claimed == 1;
    }

    public async Task RecordDeliveryAttemptAsync(
        Guid notificationId,
        bool succeeded,
        int attempts,
        string? error,
        DateTimeOffset? nextAttemptAt,
        CancellationToken cancellationToken = default)
    {
        // Conditional on Pending + the claimed attempt count: a late outcome
        // (adapter still running when an operator manually mark-sent the
        // row) can never overwrite a terminal state — zero rows updated is a
        // discarded outcome, not an error.
        if (succeeded)
        {
            var sentAt = _clock.GetUtcNow();
            await _db.Notifications
                .Where(n => n.Id == notificationId
                    && n.DeliveryStatus == NotificationDeliveryStatus.Pending
                    && n.DeliveryAttempts == attempts)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.DeliveryStatus, NotificationDeliveryStatus.Sent)
                    .SetProperty(n => n.SentAt, sentAt)
                    .SetProperty(n => n.NextAttemptAt, (DateTimeOffset?)null)
                    .SetProperty(n => n.LastDeliveryError, (string?)null),
                    cancellationToken);
            return;
        }

        // Terminal exactly when no retry is scheduled (INT-003 dead-letter);
        // PLT-044: sender-controlled error text only, truncated to the column
        // limit — never payload or credentials.
        var failedStatus = nextAttemptAt is null
            ? NotificationDeliveryStatus.Failed
            : NotificationDeliveryStatus.Pending;
        var recordedError = error is null
            ? null
            : error[..Math.Min(error.Length, MaxDeliveryErrorLength)];

        await _db.Notifications
            .Where(n => n.Id == notificationId
                && n.DeliveryStatus == NotificationDeliveryStatus.Pending
                && n.DeliveryAttempts == attempts)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DeliveryStatus, failedStatus)
                .SetProperty(n => n.NextAttemptAt, nextAttemptAt)
                .SetProperty(n => n.LastDeliveryError, recordedError),
                cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationDeliveryItem>> ListDueForDeliveryAsync(
        int limit, CancellationToken cancellationToken = default)
    {
        // PLT-052 sweep predicate: only Pending rows with a scheduled retry —
        // inline-attempt rows (NextAttemptAt NULL) and terminal states are
        // never due, so the worker cannot double-send (ADR-008 race rule).
        var now = _clock.GetUtcNow();
        return await _db.Notifications
            .Where(n => n.DeliveryStatus == NotificationDeliveryStatus.Pending
                && n.NextAttemptAt != null
                && n.NextAttemptAt <= now)
            .OrderBy(n => n.NextAttemptAt)
            .Take(limit)
            .Select(n => new NotificationDeliveryItem(
                n.Id, n.NotificationType, n.Title, n.Body, n.DeliveryAttempts))
            .ToListAsync(cancellationToken);
    }

    public async Task<NotificationMarkSentResult> MarkSentManuallyAsync(
        Guid notificationId, CancellationToken cancellationToken = default)
    {
        // One conditional UPDATE: exactly one concurrent caller performs the
        // Pending/Failed → Sent transition (TD-2 idempotent under concurrency;
        // a racing worker's late adapter outcome is discarded by its own
        // Pending guard instead of overwriting this). No adapter call
        // happens, so DeliveryAttempts stays as-is.
        var updated = await _db.Notifications
            .Where(n => n.Id == notificationId
                && (n.DeliveryStatus == NotificationDeliveryStatus.Pending
                    || n.DeliveryStatus == NotificationDeliveryStatus.Failed))
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DeliveryStatus, NotificationDeliveryStatus.Sent)
                .SetProperty(n => n.SentAt, _clock.GetUtcNow())
                .SetProperty(n => n.NextAttemptAt, (DateTimeOffset?)null)
                .SetProperty(n => n.LastDeliveryError, (string?)null),
                cancellationToken);
        if (updated == 1)
        {
            return NotificationMarkSentResult.MarkedSent;
        }

        // Zero rows: already Sent, or the row does not exist. Nothing in A0
        // transitions out of Sent, so a re-read of status distinguishes the
        // two (a row deleted in between reads as missing).
        var status = await _db.Notifications
            .Where(n => n.Id == notificationId)
            .Select(n => (string?)n.DeliveryStatus)
            .FirstOrDefaultAsync(cancellationToken);
        return status == NotificationDeliveryStatus.Sent
            ? NotificationMarkSentResult.AlreadySent
            : NotificationMarkSentResult.NotFound;
    }

    public async Task<Guid> StartAsync(string jobName, string? correlationId, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        _db.JobRuns.Add(new JobRun
        {
            Id = id,
            JobName = jobName,
            Status = "Running",
            CorrelationId = correlationId,
            StartedAt = _clock.GetUtcNow(),
        });
        await _db.SaveChangesAsync(cancellationToken);
        return id;
    }

    public async Task CompleteAsync(Guid jobRunId, CancellationToken cancellationToken = default)
    {
        var row = await _db.JobRuns.FindAsync([jobRunId], cancellationToken)
            ?? throw new InvalidOperationException($"JobRun {jobRunId} not found.");
        row.Status = "Completed";
        row.CompletedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(Guid jobRunId, string error, CancellationToken cancellationToken = default)
    {
        var row = await _db.JobRuns.FindAsync([jobRunId], cancellationToken)
            ?? throw new InvalidOperationException($"JobRun {jobRunId} not found.");
        row.Status = "Failed";
        row.Error = error;
        row.CompletedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
