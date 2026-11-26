using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.MasterData.Contracts.Location;

namespace SupplyForge.Inventory.Infrastructure.Location;

/// <summary>
/// Real implementation of <see cref="ILocationDeactivationGuard"/> (MD-042).
/// A location cannot be deactivated if any StockBalance row exists for it with OnHand > 0.
/// Lives in Inventory.Infrastructure (references MasterData.Contracts only) to avoid
/// a MasterData → Inventory module cycle.
/// </summary>
public sealed class LocationDeactivationGuard : ILocationDeactivationGuard
{
    private readonly InventoryDbContext _inventory;

    public LocationDeactivationGuard(InventoryDbContext inventory)
    {
        _inventory = inventory;
    }

    public async Task<bool> CanDeactivateAsync(Guid locationId, CancellationToken cancellationToken = default)
    {
        // A location may be deactivated only if no StockBalance holds stock at it.
        bool hasStock = await _inventory.StockBalances
            .AnyAsync(b => b.LocationId == locationId && b.OnHand > 0m, cancellationToken);

        return !hasStock;
    }
}
