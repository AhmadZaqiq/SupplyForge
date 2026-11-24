namespace SupplyForge.MasterData.Domain;

/// <summary>
/// A reason code (MD-050). Company-shared lookup — no <c>BranchId</c>
/// (PRD 02b §2.7). One global list with a unique <see cref="Code"/>:
/// adjustments (INV-040), dispositions (INB-037) and later consumers pick
/// codes from the same list (decision D9, owner-approved Q5). A purpose
/// column can be added later without breaking anything — noted in
/// A1-PROGRESS §1b, deliberately not built now.
/// </summary>
public class ReasonCode
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Deactivate, never delete (PLT-075, MD rules).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
