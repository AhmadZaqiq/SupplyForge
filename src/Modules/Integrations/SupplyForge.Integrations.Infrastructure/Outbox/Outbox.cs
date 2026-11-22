using SupplyForge.Integrations.Domain;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.Shared.Outbox;

namespace SupplyForge.Integrations.Infrastructure.Outbox;

/// <summary>
/// MOD-1a writer for OutboxMessage / InboxMessage (OUT-01).
/// <c>EnqueueAsync</c> must run inside the caller's transaction (ADR-003
/// ambient session) so the outbox row commits and rolls back with the domain
/// change; <c>IInbox</c> marks address InboxMessage, never OutboxMessage.
/// </summary>
public sealed class Outbox : IOutbox, IInbox
{
    private readonly IntegrationsDbContext _db;
    private readonly TimeProvider _clock;

    public Outbox(IntegrationsDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task EnqueueAsync(
        string eventType,
        string payloadJson,
        int version,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        // OUT-01 / A0-8 item 5: an autocommitted enqueue would survive a
        // rollback of the domain change it describes. The ambient session
        // (ADR-003) enlists this context in a shared transaction; without
        // one there is nothing to join — fail fast instead of writing.
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "IOutbox.EnqueueAsync requires an active transaction: begin an IDbSession " +
                "(IDbSessionFactory.BeginAsync) before publishing (OUT-01).");
        }

        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Version = version,
            PayloadJson = payloadJson,
            Status = "Pending",
            OccurredAt = _clock.GetUtcNow(),
            CorrelationId = correlationId,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.InboxMessages.FindAsync([messageId], cancellationToken)
            ?? throw new InvalidOperationException($"Inbox message {messageId} not found.");

        msg.Status = "Processed";
        msg.ProcessedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
    {
        var msg = await _db.InboxMessages.FindAsync([messageId], cancellationToken)
            ?? throw new InvalidOperationException($"Inbox message {messageId} not found.");

        msg.Status = "Failed";
        msg.LastError = error;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
