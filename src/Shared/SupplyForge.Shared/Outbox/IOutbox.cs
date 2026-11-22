namespace SupplyForge.Shared.Outbox;

/// <summary>
/// MOD-1a: only Integrations infrastructure writes OutboxMessage;
/// publishers call this interface inside their transaction.
/// </summary>
public interface IOutbox
{
    /// <param name="correlationId">
    /// OUT-06 / OUT-04: correlation id carried on the event (API → jobs →
    /// events → logs); null only when the publisher has no ambient
    /// correlation to attach.
    /// </param>
    Task EnqueueAsync(
        string eventType,
        string payloadJson,
        int version,
        string? correlationId,
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
