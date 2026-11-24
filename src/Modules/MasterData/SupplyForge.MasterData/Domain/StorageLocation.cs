namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Storage location within a warehouse (MD-041, MD-042).
/// Types: Receiving, Quarantine, Storage, Returns, Staging (D-16).
/// Block location flag (MD-042); no deactivate while holding stock (enforced by Inventory in A1-5).
/// </summary>
public class StorageLocation
{
    public Guid Id { get; set; }

    /// <summary>Warehouse this location belongs to (branch-owned transitively via Warehouse.BranchId).</summary>
    public Guid WarehouseId { get; set; }

    /// <summary>Location type code (DM-5): Receiving, Quarantine, Storage, Returns, Staging.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Human-readable location code, unique per warehouse.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Block location flag (MD-042). Blocked locations are excluded from eligible available stock.</summary>
    public bool IsBlocked { get; set; } = false;

    /// <summary>Active flag — deactivate not delete (MD rules). A blocked location must be inactive.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Set from the application clock (TimeProvider), no SQL default.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set from the application clock (TimeProvider), no SQL default.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Parent warehouse.</summary>
    public Warehouse Warehouse { get; set; } = null!;
}