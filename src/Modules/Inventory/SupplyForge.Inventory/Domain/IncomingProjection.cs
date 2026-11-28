namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Idempotent incoming projection of open PO quantities and expected dates
/// per product and warehouse (INV-063).
/// Planning signal only — never a stock bucket, never part of Available.
/// </summary>
public class IncomingProjection
{
    public Guid IncomingProjectionId { get; set; }

    public Guid ProductId { get; set; }

    public Guid WarehouseId { get; set; }

    /// <summary>Reference to the open purchase order (cross-module plain Guid, DM-4).</summary>
    public Guid ExpectedDeliveryId { get; set; }

    public decimal ExpectedQty { get; set; }

    public DateTimeOffset ExpectedDate { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
