using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Security;

/// <summary>
/// Default fail-closed branch scope (Q-E): no branch access, not company-wide,
/// not system. Used when no scope has been resolved for the current operation;
/// it sees nothing and can create nothing. Never used for seed/migration —
/// those use <see cref="SystemBranchScope"/> explicitly.
/// </summary>
public sealed class NoBranchScope : ICurrentBranchScope
{
    public static ICurrentBranchScope Instance { get; } = new NoBranchScope();

    private NoBranchScope() { }

    public IReadOnlyList<Guid> BranchIds { get; } = Array.Empty<Guid>();

    public bool IsCompanyWide => false;

    public bool IsSystem => false;

    public bool HasNoBranchAccess => true;

    public bool CanAccess(Guid branchId) => false;
}