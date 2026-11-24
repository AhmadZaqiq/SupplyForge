using SupplyForge.MasterData.Contracts.Location;

namespace SupplyForge.MasterData.Infrastructure.Location;

/// <summary>
/// PLACEHOLDER implementation of <see cref="ILocationDeactivationGuard"/> for A1-4.
/// It always allows deactivation because no stock data exists yet.
/// A1-5 (Inventory) MUST replace this registration with the real guard that
/// checks StockBalance for holdings (MD-042). Do not use this for production
/// deactivation logic.
/// This class exists only so the DI container can resolve the interface
/// without requiring the Inventory module at compile time (avoids a module cycle).
/// </summary>
public sealed class DefaultLocationDeactivationGuard : ILocationDeactivationGuard
{
    public Task<bool> CanDeactivateAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        // PLACEHOLDER: A1-4 only. Real guard implemented in Inventory (A1-5).
        return Task.FromResult(true);
    }
}