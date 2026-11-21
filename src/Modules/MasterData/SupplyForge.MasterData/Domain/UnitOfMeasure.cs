namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Unit of measure (MD-012). The company's units are shared master data — no
/// <c>BranchId</c> (PRD 02b §2.7). Each product names its own base unit; the
/// conversion table moves between units.
/// </summary>
public class UnitOfMeasure
{
    public Guid Id { get; set; }

    /// <summary>Short code, unique — e.g. <c>EA</c>, <c>BOX</c>, <c>KG</c>.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Deactivate, never delete (module rule, MD rules row).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
