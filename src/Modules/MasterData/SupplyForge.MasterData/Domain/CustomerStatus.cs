namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Customer status codes (MD-030; DM-5: string status codes, transitions
/// tested).
/// <para>
/// Four codes, each carrying a PRD word: Active (SAL-032 requires an Active
/// customer to confirm an order), Blocked and OnHold (SAL business rule:
/// "Blocked/On Hold customer cannot confirm" — two distinct words, so two
/// distinct codes), and Inactive for deactivate-not-delete (PLT-075).
/// Recorded as assumption D4 in A1-PROGRESS §1b; changing the set later is a
/// code change, not a schema change.
/// </para>
/// </summary>
public static class CustomerStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Blocked = "Blocked";
    public const string OnHold = "OnHold";

    public static readonly IReadOnlyList<string> All = [Active, Inactive, Blocked, OnHold];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.Ordinal);

    /// <summary>DM-5 "transitions tested": any status may move to any other status.</summary>
    public static bool CanTransition(string from, string to) =>
        IsValid(from) && IsValid(to) && !string.Equals(from, to, StringComparison.Ordinal);
}
