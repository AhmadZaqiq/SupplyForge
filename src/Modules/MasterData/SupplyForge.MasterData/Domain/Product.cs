namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Product master record (MD-010). Company-shared — no <c>BranchId</c>
/// (PRD 02b §2.7). Quantities are base-unit with scale 4 and money follows the
/// base-currency scale (DM-2, decision D-j in A1-PROGRESS §1a).
/// </summary>
public class Product
{
    public Guid Id { get; set; }

    /// <summary>Stock-keeping code, unique — the handle every later module uses.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Nullable: uncategorised products are legal (decision D-e).</summary>
    public Guid? CategoryId { get; set; }

    public Category? Category { get; set; }

    /// <summary>MD-012 "store base unit" — required.</summary>
    public Guid BaseUnitOfMeasureId { get; set; }

    public UnitOfMeasure BaseUnitOfMeasure { get; set; } = null!;

    /// <summary>MD-010: goods must be inspected before putaway (INB).</summary>
    public bool RequiresInspection { get; set; }

    /// <summary>MD-010 reorder point, in base units (INV-060 consumes it).</summary>
    public decimal ReorderPoint { get; set; }

    /// <summary>MD-010 supplier lead time, in days.</summary>
    public int LeadTimeDays { get; set; }

    /// <summary>MD-010 default sell price in the base currency (A-1 scale 2).</summary>
    public decimal DefaultPrice { get; set; }

    /// <summary>MD-010 standard cost — the opening basis for WAC (A1-6).</summary>
    public decimal StandardCost { get; set; }

    /// <summary>MD-013 status stored as a string code (DM-5).</summary>
    public string Status { get; set; } = ProductStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
