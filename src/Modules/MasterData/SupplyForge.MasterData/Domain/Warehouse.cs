namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Warehouse (MD-040): belongs to a branch; the schema supports N branches (A-2),
/// v1 seeds three. Warehouses are deactivated, never deleted (MD rules).
/// </summary>
public class Warehouse
{
    public Guid Id { get; set; }

    /// <summary>Branch this warehouse belongs to (DM-1).</summary>
    public Guid BranchId { get; set; }

    /// <summary>Human-readable warehouse code, unique company-wide (A1-3 convention).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Active flag — deactivate not delete (MD rules, DM-5).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Set from the application clock (TimeProvider), no SQL default.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set from the application clock (TimeProvider), no SQL default.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>Branch this warehouse belongs to.</summary>
    public Branch Branch { get; set; } = null!;

    /// <summary>Locations in this warehouse.</summary>
    public ICollection<StorageLocation> Locations { get; set; } = new List<StorageLocation>();
}