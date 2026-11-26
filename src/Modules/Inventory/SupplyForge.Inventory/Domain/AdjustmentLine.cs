namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Line on an Adjustment document (INV-040).
/// Qty positive for AdjustmentIn, negative for AdjustmentOut.
/// </summary>
public class AdjustmentLine
{
    public Guid AdjustmentLineId { get; set; }
    public Guid AdjustmentId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid LocationId { get; set; }
    public decimal Qty { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
