namespace SupplyForge.Inventory.Domain;

public class StockMovement
{
    public Guid MovementId { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid LocationId { get; set; }
    public Guid BranchId { get; set; }
    public decimal Qty { get; set; }
    public decimal UnitCost { get; set; }
    public string SourceDocumentType { get; set; } = string.Empty;
    public Guid? SourceDocumentId { get; set; }
    public Guid? ReversalOfMovementId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public static class MovementTypes
    {
        public const string Receipt = "Receipt";
        public const string Issue = "Issue";
        public const string TransferOut = "TransferOut";
        public const string TransferIn = "TransferIn";
        public const string AdjustmentIn = "AdjustmentIn";
        public const string AdjustmentOut = "AdjustmentOut";
        public const string OpeningBalance = "OpeningBalance";
    }
}