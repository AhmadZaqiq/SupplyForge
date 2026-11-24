namespace SupplyForge.MasterData.Domain;

/// <summary>
/// A payment term (MD-050). Company-shared lookup — no <c>BranchId</c>
/// (PRD 02b §2.7). Finance derives an invoice due date from
/// <see cref="DueDays"/> (FIN-021).
/// </summary>
public class PaymentTerm
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Days until an invoice is due. Schema CHECK keeps it non-negative.</summary>
    public int DueDays { get; set; }

    /// <summary>Deactivate, never delete (PLT-075, MD rules).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
