namespace SupplyForge.Shared.Auditing;

/// <summary>
/// MOD-1a: AuditLog is a shared table owned by Platform Core infrastructure.
/// Other modules write only through this interface (inside the TXN).
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(
        string actorType,
        string? actorId,
        string action,
        string? entityType,
        string? entityId,
        string? branchId,
        string? detailsJson,
        CancellationToken cancellationToken = default);
}
