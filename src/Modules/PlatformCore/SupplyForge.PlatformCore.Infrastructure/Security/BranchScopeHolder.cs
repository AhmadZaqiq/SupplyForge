using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Security;

/// <summary>
/// Scoped holder for the current branch scope (A1-4 Part 2, Q-E option a).
/// One instance per request / operation scope. The middleware sets Scope per
/// request; the design-time factory and tests default to the fail-closed
/// NoBranchScope. The explicit system scope is set only by the Host for seed,
/// migration and background jobs — never from an HTTP request.
/// </summary>
public sealed class BranchScopeHolder
{
    /// <summary>
    /// The scope for the current scope lifetime. Defaults to fail-closed so
    /// nothing is visible and nothing can be created until a scope is set.
    /// </summary>
    public ICurrentBranchScope Scope { get; set; } = NoBranchScope.Instance;
}
