namespace SupplyForge.PlatformCore.Contracts.Security;

/// <summary>
/// Represents the current branch scope for the request or background operation.
/// Implemented by the Host/PlatformCore.Infrastructure; consumed by modules
/// that own branch-scoped data (Master Data warehouses/locations, Inventory, etc.).
/// </summary>
public interface ICurrentBranchScope
{
    /// <summary>
    /// Current branch id for the request, or null if company-wide scope.
    /// </summary>
    Guid? BranchId { get; }

    /// <summary>
    /// True when the caller has company-wide permission for the current action (BRN-010).
    /// </summary>
    bool IsCompanyWide { get; }

    /// <summary>
    /// True when the caller has no branch assignments (sees nothing).
    /// </summary>
    bool HasNoBranchAccess { get; }
}