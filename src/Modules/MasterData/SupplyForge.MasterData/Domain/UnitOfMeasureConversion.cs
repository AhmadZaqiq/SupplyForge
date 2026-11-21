namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Conversion between two units of measure (MD-012): one <c>FromUnit</c>
/// equals <c>Factor</c> <c>ToUnit</c>s. Company-shared, no <c>BranchId</c>
/// (PRD 02b §2.7).
/// <para>
/// The table carries a CHECK constraint (<c>Factor &gt; 0</c>) because a
/// zero or negative factor silently corrupts every quantity conversion —
/// see A1-PROGRESS §1a D-h.
/// </para>
/// </summary>
public class UnitOfMeasureConversion
{
    public Guid Id { get; set; }

    public Guid FromUnitId { get; set; }

    public UnitOfMeasure FromUnit { get; set; } = null!;

    public Guid ToUnitId { get; set; }

    public UnitOfMeasure ToUnit { get; set; } = null!;

    /// <summary>How many To-units one From-unit equals. Qty scale 4 (DM-2).</summary>
    public decimal Factor { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
