namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Customer master record (MD-030). Company-shared — no <c>BranchId</c>
/// (PRD 02b §2.7). The credit limit is stored here as master data; Finance
/// owns credit exposure (MOD-4) and reads this value through contracts later.
/// </summary>
public class Customer
{
    public Guid Id { get; set; }

    /// <summary>Short unique code — the handle seeds, tests and later modules use.</summary>
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>MD-030 status stored as a string code (DM-5).</summary>
    public string Status { get; set; } = CustomerStatus.Active;

    /// <summary>
    /// MD-030 credit limit in the base currency (DM-2, A-1 scale 2).
    /// NOT NULL with database default 0: 0 means "no credit allowed" and P1 has
    /// no unlimited value (owner answer Q1, TD-3 fails closed).
    /// </summary>
    public decimal CreditLimit { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
