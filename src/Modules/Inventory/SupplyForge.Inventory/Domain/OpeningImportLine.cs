namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Line on an OpeningImport document (INV-014).
/// LocationId is required because StockMovementPoster locks StockBalance by
/// (ProductId, WarehouseId, LocationId).
/// </summary>
public class OpeningImportLine
{
    public Guid OpeningImportLineId { get; set; }
    public Guid OpeningImportId { get; set; }
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid LocationId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
