namespace SupplyForge.PlatformCore.Contracts.Security;

/// <summary>
/// Represents the current branch scope for the request or background operation
/// (A1-4 Part 2, BRN-010, Q-D, Q-E option a).
/// Implemented by the Host/PlatformCore.Infrastructure; consumed by modules
/// that own branch-scoped data (Master Data warehouses/locations, Inventory, etc.).
/// <para>
/// Fail closed by default (Q-E): a request with no scope sees nothing and can
/// create nothing. An explicit system scope exists only for seed, migration and
/// later background jobs — it is created by <c>SystemBranchScope</c> and is
/// never reachable from HTTP endpoints.
/// </para>
/// </summary>
public interface ICurrentBranchScope
{
    /// <summary>
    /// Branch ids the caller may access. Empty = no branch access (sees nothing).
    /// Ignored when <see cref="IsCompanyWide"/> or <see cref="IsSystem"/> is true.
    /// </summary>
    IReadOnlyList<Guid> BranchIds { get; }

    /// <summary>
    /// True when the caller has company-wide permission for the current action (BRN-010).
    /// </summary>
    bool IsCompanyWide { get; }

    /// <summary>
    /// True only for the explicit system scope (seed, migration, background jobs).
    /// Never produced from an HTTP request (Q-E option a).
    /// </summary>
    bool IsSystem { get; }

    /// <summary>
    /// True when the caller has no branch assignments and is not company-wide / system
    /// (fail closed: sees nothing).
    /// </summary>
    bool HasNoBranchAccess { get; }

    /// <summary>
    /// True when the caller may access the given branch id.
    /// Company-wide and system scope can access every branch.
    /// </summary>
    bool CanAccess(Guid branchId);
}