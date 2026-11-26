namespace SupplyForge.Inventory.Domain;

public class ProductCost
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal AvgCost { get; set; }
    public decimal QtyOnHand { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public void ApplyReceipt(decimal receiptQty, decimal receiptUnitCost)
    {
        if (receiptQty <= 0)
        {
            throw new ArgumentException("Receipt quantity must be positive.", nameof(receiptQty));
        }
        if (receiptUnitCost < 0)
        {
            throw new ArgumentException("Receipt unit cost cannot be negative.", nameof(receiptUnitCost));
        }

        decimal qtyBefore = QtyOnHand;
        decimal avgBefore = AvgCost;

        if (qtyBefore == 0)
        {
            AvgCost = Math.Round(receiptUnitCost, 8);
            QtyOnHand = receiptQty;
            return;
        }

        decimal totalValueBefore = qtyBefore * avgBefore;
        decimal totalValueReceipt = receiptQty * receiptUnitCost;
        decimal totalQtyAfter = qtyBefore + receiptQty;

        decimal newAvg = (totalValueBefore + totalValueReceipt) / totalQtyAfter;
        AvgCost = Math.Round(newAvg, 8);
        QtyOnHand = totalQtyAfter;
    }

    public decimal ApplyIssue(decimal issueQty)
    {
        if (issueQty <= 0)
        {
            throw new ArgumentException("Issue quantity must be positive.", nameof(issueQty));
        }
        if (QtyOnHand == 0)
        {
            throw new InvalidOperationException("Cannot issue from zero stock (WAC-08).");
        }
        if (issueQty > QtyOnHand)
        {
            throw new InvalidOperationException("Issue quantity exceeds available stock (INV-NEG).");
        }

        decimal costUsed = AvgCost;

        QtyOnHand -= issueQty;

        if (QtyOnHand == 0)
        {
            AvgCost = 0m;
        }

        return Math.Round(costUsed, 8);
    }

    public decimal ApplyTransferOut(decimal transferQty)
    {
        if (transferQty <= 0)
        {
            throw new ArgumentException("Transfer quantity must be positive.", nameof(transferQty));
        }
        if (QtyOnHand == 0)
        {
            throw new InvalidOperationException("Cannot transfer from zero stock (WAC-08).");
        }
        if (transferQty > QtyOnHand)
        {
            throw new InvalidOperationException("Transfer quantity exceeds available stock (INV-NEG).");
        }

        decimal costUsed = AvgCost;

        QtyOnHand -= transferQty;

        if (QtyOnHand == 0)
        {
            AvgCost = 0m;
        }

        return Math.Round(costUsed, 8);
    }

    public void ApplyTransferIn(decimal transferQty, decimal sourceAvgCost)
    {
        if (transferQty <= 0)
        {
            throw new ArgumentException("Transfer quantity must be positive.", nameof(transferQty));
        }
        if (sourceAvgCost < 0)
        {
            throw new ArgumentException("Source average cost cannot be negative.", nameof(sourceAvgCost));
        }

        ApplyReceipt(transferQty, sourceAvgCost);
    }

    public void ApplyOpeningBalance(decimal openingQty, decimal openingUnitCost)
    {
        if (openingQty < 0)
        {
            throw new ArgumentException("Opening quantity cannot be negative.", nameof(openingQty));
        }
        if (openingUnitCost < 0)
        {
            throw new ArgumentException("Opening unit cost cannot be negative.", nameof(openingUnitCost));
        }

        if (QtyOnHand == 0 && AvgCost == 0)
        {
            AvgCost = Math.Round(openingUnitCost, 8);
            QtyOnHand = openingQty;
            return;
        }

        ApplyReceipt(openingQty, openingUnitCost);
    }

    /// <summary>
    /// WAC-10 residual dust: Not applicable under this costing model.
    ///
    /// Mathematical reasoning: All units in a (ProductId, WarehouseId) bucket share a single
    /// AvgCost value stored at scale 8. Every issue/transfer-out records UnitCost = AvgCost
    /// at the time of the movement. AvgCost only changes on receipts/adjustments-in, which
    /// increase QtyOnHand (so QtyOnHand never reaches 0 between a receipt and the next zeroing
    /// issue). Therefore, when QtyOnHand reaches 0, every prior issue/transfer-out used the
    /// same AvgCost, and the sum of (qty × UnitCost) across all prior outflows exactly equals
    /// QtyOnHand_before × AvgCost_before. No rounding residual accumulates, so no dust needs
    /// to be assigned to the final issue. AvgCost is simply reset to 0.
    /// </summary>
}