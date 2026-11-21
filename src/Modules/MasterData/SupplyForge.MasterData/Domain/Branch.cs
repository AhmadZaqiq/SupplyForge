namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Branch (MD-002): the schema supports N branches, the v1 seed inserts three
/// (A-2), and there is no hard-coded maximum. Warehouses require a branch.
/// <para>
/// <c>Id</c> is a <see cref="Guid"/> because Platform Core already stores
/// <c>UserBranchScope.BranchId</c> as a <see cref="Guid"/> in an applied
/// migration — branch scoping must resolve to these ids (see A1-PROGRESS §0 B4
/// for the DM-3 BIGINT conflict this pins down).
/// </para>
/// Branches are deactivated, never deleted (MD-004/MD-040 convention).
/// </summary>
public class Branch
{
    public Guid Id { get; set; }

    /// <summary>Human-readable branch code, unique (Q-3: placeholder in v1).</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
