namespace SupplyForge.Shared.Outbox;

/// <summary>
/// MOD-1a: only Integrations infrastructure writes OutboxMessage;
/// publishers call this interface inside their transaction.
/// </summary>
public interface IOutbox
{
    Task EnqueueAsync(
        string eventType,
        string payloadJson,
        int version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// MOD-1a: only Integrations infrastructure writes InboxMessage.
/// </summary>
public interface IInbox
{
    Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default);

    Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default);
}
