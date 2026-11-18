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
        var row = await _db.IdempotencyKeys.FindAsync([key], cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency key {key} not found.");

        row.Status = "Completed";
        row.ResponseSnapshotJson = responseSnapshotJson;
        row.CompletedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task FailAsync(string key, CancellationToken cancellationToken = default)
    {
        var row = await _db.IdempotencyKeys.FindAsync([key], cancellationToken)
            ?? throw new InvalidOperationException($"Idempotency key {key} not found.");

        row.Status = "Failed";
        row.CompletedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> NextAsync(
        string scope,
        string prefix,
        int padWidth,
        CancellationToken cancellationToken = default)
    {
        // Atomic increment under the ambient transaction when one is active;
        // single-row upsert otherwise.
        var seq = await _db.DocumentSequences
            .FirstOrDefaultAsync(x => x.Scope == scope, cancellationToken);

        if (seq is null)
        {
            seq = new DocumentSequence { Scope = scope, LastValue = 1, UpdatedAt = _clock.GetUtcNow() };
            _db.DocumentSequences.Add(seq);
        }
        else
        {
            seq.LastValue++;
            seq.UpdatedAt = _clock.GetUtcNow();
            _db.DocumentSequences.Update(seq);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return $"{prefix}{seq.LastValue.ToString().PadLeft(padWidth, '0')}";
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
