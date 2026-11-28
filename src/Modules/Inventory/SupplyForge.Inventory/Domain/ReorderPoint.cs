namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Reorder point per product and optionally warehouse (INV-060).
/// When Available + Incoming falls below ReorderPointQty, a StockBelowReorderPoint
/// event is raised. Alert once per breach until recovered (INV-061).
/// WarehouseId is null for a company-wide reorder point.
/// </summary>
public class ReorderPoint
{
    public Guid ReorderPointId { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Nullable for company-wide reorder point (applies to all warehouses).</summary>
    public Guid? WarehouseId { get; set; }

    public decimal ReorderPointQty { get; set; }

    /// <summary>True when an alert is currently active for this breach.</summary>
    public bool IsAlerted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
