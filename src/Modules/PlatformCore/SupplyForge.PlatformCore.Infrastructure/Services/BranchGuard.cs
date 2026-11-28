using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Services;

/// <summary>
/// Central branch-guard implementation (BRN-011…014). Validates branch access
/// using <see cref="ICurrentBranchScope"/>. Lives in PlatformCore.Infrastructure
/// so that the contracts assembly does not depend on MasterData.
/// </summary>
public sealed class BranchGuard : IBranchGuard
{
    private readonly ICurrentBranchScope _branchScope;

    public BranchGuard(ICurrentBranchScope branchScope)
    {
        _branchScope = branchScope;
    }

    /// <inheritdoc />
    public void RequireAccess(Guid branchId)
    {
        // System scope and company-wide scope can access every branch.
        if (_branchScope.IsSystem || _branchScope.IsCompanyWide)
        {
            return;
        }

        // Branch-scoped callers can only access branches in their scope.
        if (!_branchScope.CanAccess(branchId))
        {
            throw new BranchNotFoundException($"Branch {branchId} not found or not accessible (BRN-012).");
        }
    }
}
