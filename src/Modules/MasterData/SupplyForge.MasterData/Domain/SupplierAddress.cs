namespace SupplyForge.MasterData.Domain;

/// <summary>
/// One address of a supplier (MD-031 "addresses with defaults"). Separate
/// table from <see cref="CustomerAddress"/> because DM-9.2 names
/// <c>SupplierAddress</c> and <c>CustomerAddress</c> as distinct entities.
/// Company-shared master data: no <c>BranchId</c> (PRD 02b §2.7).
/// </summary>
public class SupplierAddress
{
    public Guid Id { get; set; }

    public Guid SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public string Line1 { get; set; } = string.Empty;

    public string? Line2 { get; set; }

    public string City { get; set; } = string.Empty;

    public string? Region { get; set; }

    /// <summary>Nullable: many countries issue no postal code (decision D7).</summary>
    public string? PostalCode { get; set; }

    public string Country { get; set; } = string.Empty;

    /// <summary>MD-031 default shipping address (decision D6, owner-approved Q2).</summary>
    public bool IsDefaultShipping { get; set; }

    /// <summary>MD-031 default billing address (decision D6, owner-approved Q2).</summary>
    public bool IsDefaultBilling { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
