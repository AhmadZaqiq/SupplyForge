namespace SupplyForge.MasterData.Infrastructure.Data;

/// <summary>
/// Default <see cref="SupplyForge.PlatformCore.Contracts.Security.ICurrentBranchScope"/>
/// values for Master Data (A1-4 Part 2). Kept in Master Data so the module
/// does not reference Platform Core Infrastructure (MOD-1): the fail-closed
/// no-scope is the default for design-time and tests, and the explicit system
/// scope (Q-E option a) is created by Platform Core and set by the Host.
/// </summary>
public static class BranchScopeDefaults
{
    /// <summary>Fail-closed scope: no branch access (Q-E).</summary>
    public static SupplyForge.PlatformCore.Contracts.Security.ICurrentBranchScope NoScope { get; } =
        new NoScopeImpl();
}