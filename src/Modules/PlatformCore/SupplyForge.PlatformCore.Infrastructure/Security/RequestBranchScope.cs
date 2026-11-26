using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Security;

/// <summary>
/// Request-scoped branch scope built from the caller's <c>UserBranchScope</c>
/// rows (BRN-010). Fail closed by default (Q-E): no rows and not company-wide
/// means <see cref="HasNoBranchAccess"/> is true and nothing is visible.
/// The system scope is a different type (<see cref="SystemBranchScope"/>) and
/// is never produced from an HTTP request.
/// </summary>
public sealed class RequestBranchScope : ICurrentBranchScope
{
    public RequestBranchScope(IReadOnlyList<Guid> branchIds, bool isCompanyWide)
    {
        BranchIds = branchIds ?? Array.Empty<Guid>();
        IsCompanyWide = isCompanyWide;
    }

    public IReadOnlyList<Guid> BranchIds { get; }

    public bool IsCompanyWide { get; }

    public bool IsSystem => false;

    public bool HasNoBranchAccess => !IsCompanyWide && BranchIds.Count == 0;

    public bool CanAccess(Guid branchId) =>
        IsCompanyWide || BranchIds.Contains(branchId);
}