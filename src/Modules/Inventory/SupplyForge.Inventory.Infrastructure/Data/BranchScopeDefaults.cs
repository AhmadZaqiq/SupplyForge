namespace SupplyForge.Inventory.Infrastructure.Data;

public static class BranchScopeDefaults
{
    public static SupplyForge.PlatformCore.Contracts.Security.ICurrentBranchScope NoScope { get; } =
        new NoScopeImpl();
}