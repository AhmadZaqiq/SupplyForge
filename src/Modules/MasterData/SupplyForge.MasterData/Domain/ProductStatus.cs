namespace SupplyForge.MasterData.Domain;

/// <summary>
/// Product status codes (MD-013; DM-5: "explicit status enums stored as string
/// status codes; transitions tested").
/// <para>
/// The PRD names no values anywhere (<c>grep -rn "Archiv" docs/prd/</c> returns
/// nothing), so these three are the smallest set that expresses MD-013's own
/// vocabulary — recorded as assumption D-b in A1-PROGRESS §1a. The stored value
/// is always one of <see cref="All"/>; changing the set later is a code change,
/// not a schema change.
/// </para>
/// </summary>
public static class ProductStatus
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Archived = "Archived";

    public static readonly IReadOnlyList<string> All = [Active, Inactive, Archived];

    public static bool IsValid(string status) => All.Contains(status, StringComparer.Ordinal);

    /// <summary>
    /// DM-5 "transitions tested": any status may move to any other status.
    /// MD-013 states exactly one constraint on the whole status model — the
    /// archive guard in <see cref="CanArchive"/> — so nothing else is
    /// restricted here. Staying put is not a transition.
    /// </summary>
    public static bool CanTransition(string from, string to) =>
        IsValid(from) && IsValid(to) && !string.Equals(from, to, StringComparison.Ordinal);

    /// <summary>
    /// MD-013 "no archive with on-hand &gt; 0".
    /// <para>
    /// Pure rule: the caller supplies on-hand, because the inventory ledger
    /// that owns the real number arrives in A1-5 (decision D-d). A product
    /// with no stock yet has on-hand 0, so it may be archived.
    /// </para>
    /// </summary>
    public static bool CanArchive(decimal onHand) => onHand <= 0m;
}
