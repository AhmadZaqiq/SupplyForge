using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.Inventory.Infrastructure.Data;

internal sealed class NoScopeImpl : ICurrentBranchScope
{
    public IReadOnlyList<Guid> BranchIds { get; } = Array.Empty<Guid>();

    public bool IsCompanyWide => false;

    public bool IsSystem => false;

    public bool HasNoBranchAccess => true;

    public bool CanAccess(Guid branchId) => false;
}