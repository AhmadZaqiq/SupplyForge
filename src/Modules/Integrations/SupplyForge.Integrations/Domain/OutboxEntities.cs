namespace SupplyForge.Integrations.Domain;

/// <summary>
/// MOD-1a: OutboxMessage / InboxMessage owned by Integrations infrastructure.
/// Publishers enqueue via IOutbox inside their transaction (MOD-3, OUT-01).
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int Version { get; set; } = 1;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public string? CorrelationId { get; set; }
}

public class InboxMessage
{
    public Guid Id { get; set; }
    /// <summary>Consumer-side idempotency (message id or hash).</summary>
    public string MessageKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Received";
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}

public class IntegrationLog
{
    public Guid Id { get; set; }
    public string Adapter { get; set; } = string.Empty;
    public string Level { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? CorrelationId { get; set; }
}
