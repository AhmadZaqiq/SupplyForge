namespace SupplyForge.Inventory.Domain;

/// <summary>
/// Opening balance import document (INV-014).
/// Statuses: Draft → Validated (dry-run) → Posted → Rejected.
/// </summary>
public class OpeningImport
{
    public Guid OpeningImportId { get; set; }

    public string Status { get; set; } = string.Empty;

    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public static class Statuses
    {
        public const string Draft = "Draft";
        public const string Validated = "Validated";
        public const string Posted = "Posted";
        public const string Rejected = "Rejected";
    }
}
