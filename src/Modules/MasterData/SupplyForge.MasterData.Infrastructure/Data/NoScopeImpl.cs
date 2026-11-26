using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.MasterData.Infrastructure.Data;

/// <summary>
/// Fail-closed branch scope (Q-E): no branch access, not company-wide, not
/// system. Sees nothing and can create nothing.
/// </summary>
internal sealed class NoScopeImpl : ICurrentBranchScope
{
    public IReadOnlyList<Guid> BranchIds { get; } = Array.Empty<Guid>();

    public bool IsCompanyWide => false;

    public bool IsSystem => false;

    public bool HasNoBranchAccess => true;

    public bool CanAccess(Guid branchId) => false;
}