namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Supplier master record (MD-020). Company-shared — no <c>BranchId</c>
/// (PRD 02b §2.7). Single base currency only: no currency column, per D-12.
/// Blocked/Active behaviour lives in A2 (decision D13, A1-PROGRESS §1b) —
/// this increment only stores the status code.
/// </summary>
public class Supplier
{
    public Guid Id { get; set; }

    /// <summary>Short unique code — the handle seeds, tests and later modules use.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>MD-020 status stored as a string code (DM-5).</summary>
    public string Status { get; set; } = SupplierStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
