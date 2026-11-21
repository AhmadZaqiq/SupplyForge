namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Category hierarchy (MD-011): a self-referencing parent link, so any depth
/// is representable without a separate table.
/// <para>
/// Company-shared master data — no <c>BranchId</c> (PRD 02b §2.7).
/// </para>
/// </summary>
public class Category
{
    public Guid Id { get; set; }

    /// <summary>Human code, unique (DM-3: humans read codes, keys stay opaque).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Null = root category.</summary>
    public Guid? ParentId { get; set; }

    public Category? Parent { get; set; }

    public ICollection<Category> Children { get; set; } = new List<Category>();

    /// <summary>Deactivate, never delete (module rule, MD rules row).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
