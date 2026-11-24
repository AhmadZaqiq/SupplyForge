namespace SupplyForge.MasterData.Domain;

/// <summary>
/// A configured tax rate (MD-050). Company-shared lookup — no <c>BranchId</c>
/// (PRD 02b §2.7). Rates are configuration only: no jurisdiction, no
/// compliance claim (Q-2, FIN-CALC §6.6.6).
/// </summary>
public class TaxRate
{
    public Guid Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Percentage with six digits after the decimal point (FIN-CALC-10):
    /// <c>15.000000</c> means 15 %. Schema CHECK keeps it within 0…100.
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>Deactivate, never delete (PLT-075, MD rules).</summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
