using SupplyForge.Integrations.Domain;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.Shared.Outbox;

namespace SupplyForge.Integrations.Infrastructure.Outbox;

/// <summary>
/// MOD-1a writer for OutboxMessage / InboxMessage (OUT-01).
/// Callers must already be inside the ambient transaction (ADR-003).
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
        CancellationToken cancellationToken = default)
    {
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            Version = version,
            PayloadJson = payloadJson,
            Status = "Pending",
            OccurredAt = _clock.GetUtcNow(),
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.OutboxMessages.FindAsync([messageId], cancellationToken)
            ?? throw new InvalidOperationException($"Outbox message {messageId} not found.");

        msg.Status = "Processed";
        msg.ProcessedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
    {
        var msg = await _db.OutboxMessages.FindAsync([messageId], cancellationToken)
            ?? throw new InvalidOperationException($"Outbox message {messageId} not found.");

        msg.Status = "Failed";
        msg.AttemptCount++;
        msg.LastError = error;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkInboxProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var msg = await _db.InboxMessages.FindAsync([messageId], cancellationToken)
            ?? throw new InvalidOperationException($"Inbox message {messageId} not found.");

        msg.Status = "Processed";
        msg.ProcessedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
    }
}
