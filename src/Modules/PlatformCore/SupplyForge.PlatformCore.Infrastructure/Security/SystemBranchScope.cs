using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Security;

/// <summary>
/// Explicit system branch scope (Q-E option a). Grants access to every branch
/// for seed, migration and later background jobs. It is created only by
/// <see cref="SystemBranchScope.Create"/> and is never produced from an HTTP
/// request — request scopes fail closed (BRN-010, Q-E).
/// </summary>
public sealed class SystemBranchScope : ICurrentBranchScope
{
    private SystemBranchScope() { }

    public static ICurrentBranchScope Create() => new SystemBranchScope();

    public IReadOnlyList<Guid> BranchIds { get; } = Array.Empty<Guid>();

    public bool IsCompanyWide => true;

    public bool IsSystem => true;

    public bool HasNoBranchAccess => false;

    public bool CanAccess(Guid branchId) => true;
}