namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Adjustment document (INV-040, INV-032).
/// Two kinds:
/// 1. Normal adjustment (no SourceDocumentType): posts AdjustmentIn/AdjustmentOut movements via poster.
/// 2. Transfer-shortfall reconciliation (SourceDocumentType = "Transfer", SourceDocumentId = TransferId):
///    audit/documentation only, Status = Posted, creates no StockMovements.
/// Statuses: Draft → PendingApproval → Approved → Posted → Rejected.
/// </summary>
public class Adjustment
{
    public Guid AdjustmentId { get; set; }

    public string ReasonCode { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>"Transfer" for INV-032 linkage; null for normal adjustments.</summary>
    public string? SourceDocumentType { get; set; }

    /// <summary>Linked TransferId when SourceDocumentType = "Transfer".</summary>
    public Guid? SourceDocumentId { get; set; }

    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public static class Statuses
    {
        public const string Draft = "Draft";
        public const string PendingApproval = "PendingApproval";
        public const string Approved = "Approved";
        public const string Posted = "Posted";
        public const string Rejected = "Rejected";
    }
}
