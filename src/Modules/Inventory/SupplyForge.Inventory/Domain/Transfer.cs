namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Transfer document (INV-030, INV-031, INV-032, BRN-020–023).
/// Same-warehouse location move: Draft → Approved → Closed (both legs posted atomically).
/// Inter-warehouse: Draft → Approved → In Transit → Received (partial) → Closed → Cancelled.
/// Cross-module references are plain Guid columns (DM-4); no EF FK across module schemas.
/// </summary>
public class Transfer
{
    public Guid TransferId { get; set; }

    public Guid SourceWarehouseId { get; set; }
    public Guid SourceLocationId { get; set; }
    public Guid DestWarehouseId { get; set; }
    public Guid DestLocationId { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>Total transfer quantity (&gt; 0).</summary>
    public decimal Qty { get; set; }

    /// <summary>Qty shipped at source (set on TransferOut post).</summary>
    public decimal ShippedQty { get; set; }

    /// <summary>Cumulative qty received at destination.</summary>
    public decimal ReceivedQty { get; set; }

    public string Status { get; set; } = string.Empty;

    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    /// <summary>ShippedQty − ReceivedQty (computed, not stored).</summary>
    public decimal RemainingQty => ShippedQty - ReceivedQty;

    public static class Statuses
    {
        public const string Draft = "Draft";
        public const string Approved = "Approved";
        public const string InTransit = "InTransit";
        public const string Received = "Received";
        public const string Closed = "Closed";
        public const string Cancelled = "Cancelled";
    }
}
