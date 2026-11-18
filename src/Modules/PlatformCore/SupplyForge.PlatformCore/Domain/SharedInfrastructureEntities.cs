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
