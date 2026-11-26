namespace SupplyForge.Inventory.Domain;

public class StockBalance
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid LocationId { get; set; }
    public decimal OnHand { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}