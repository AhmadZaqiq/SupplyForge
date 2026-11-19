namespace SupplyForge.PlatformCore.Domain;

/// <summary>
/// MOD-1a shared tables owned by Platform Core infrastructure
/// (audit, notifications, sequences, idempotency, jobs). Other modules
/// write only via Shared interfaces.
/// </summary>
public class AuditLog
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public string? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? BranchId { get; set; }
    /// <summary>JSON; must be redacted (PLT-043) before persist.</summary>
    public string? DetailsJson { get; set; }
    public string? CorrelationId { get; set; }
}

public class Notification
{
    public Guid Id { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public Guid? BranchId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? Link { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }

    // ADR-008 / PLT-051 / PLT-052 delivery state (A0 gate round-trip legs
    // "mark-sent path works" and "failure injection shows Failed").
    // Pending = in-app stored, email not yet delivered (or awaiting retry);
    // Sent = adapter accepted it or manual mark-sent (TD-2);
    // Failed = max attempts exhausted (dead-letter, visible per INT-003).
    public string DeliveryStatus { get; set; } = NotificationDeliveryStatus.Pending;
    public int DeliveryAttempts { get; set; }
    /// <summary>When the next retry is due; NULL = nothing scheduled
    /// (initial row or terminal state — the sweep never picks it up).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    /// <summary>Last adapter error, truncated (PLT-044: sender-controlled
    /// text only, never payload or credentials).</summary>
    public string? LastDeliveryError { get; set; }
}

/// <summary>Values of <see cref="Notification.DeliveryStatus"/> (ADR-008).</summary>
public static class NotificationDeliveryStatus
{
    public const string Pending = "Pending";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}

public class DocumentSequence
{
    public string Scope { get; set; } = string.Empty;
    public long LastValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class IdempotencyKey
{
    public string Key { get; set; } = string.Empty;
    public string PayloadHash { get; set; } = string.Empty;
    public string Status { get; set; } = "InProgress";
    public string? ResponseSnapshotJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public class JobRun
{
    public Guid Id { get; set; }
    public string JobName { get; set; } = string.Empty;
    public string Status { get; set; } = "Running";
    public string? CorrelationId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? Error { get; set; }
}
