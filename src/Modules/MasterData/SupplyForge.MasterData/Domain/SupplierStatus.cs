namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Supplier status codes (MD-020; DM-5: "explicit status enums stored as string
/// status codes; transitions tested").
/// <para>
/// The PRD states "Supplier + status" and the rule "Blocked supplier/customer
/// rules" without listing values, so these three are the smallest set that
/// carries the PRD's own vocabulary: Active (PRC: "Active supplier only after
/// approval"), Blocked (MD rules), and Inactive for deactivate-not-delete
/// (PLT-075). Recorded as assumption D4 in A1-PROGRESS §1b. What Blocked does
/// to a supplier is parked to A2 (Q3) — no approval logic here.
/// </para>
/// </summary>
public static class SupplierStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Blocked = "Blocked";

    public static readonly IReadOnlyList<string> All = [Active, Inactive, Blocked];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.Ordinal);

    /// <summary>DM-5 "transitions tested": any status may move to any other status.</summary>
    public static bool CanTransition(string from, string to) =>
        IsValid(from) && IsValid(to) && !string.Equals(from, to, StringComparison.Ordinal);
}
