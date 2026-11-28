using System.Linq;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.Inventory.Contracts.Availability;
using SupplyForge.MasterData.Contracts.Location;

namespace SupplyForge.Inventory.Infrastructure;

/// <summary>
/// Shared availability calculation (INV-100).
/// Used by both <see cref="AvailabilityQueryService"/> and
/// <see cref="ReplenishmentService"/> to avoid duplicated calculation logic.
/// </summary>
public sealed class AvailabilityCalculator
{
    private readonly InventoryDbContext _inventory;
    private readonly ILocationQuery _locationQuery;

    public AvailabilityCalculator(
        InventoryDbContext inventory,
        ILocationQuery locationQuery)
    {
        _inventory = inventory;
        _locationQuery = locationQuery;
    }

    /// <summary>
    /// Computes availability for a product at a warehouse.
    /// Returns on-hand, available (eligible on-hand minus reserved),
    /// reserved, blocked, in-transit, incoming qty and incoming date.
    /// </summary>
    public async Task<AvailabilitySnapshot> CalculateAsync(
        Guid productId,
        Guid? warehouseId,
        CancellationToken ct = default)
    {
        var balances = await _inventory.StockBalances
            .Where(b => b.ProductId == productId)
            .Where(b => warehouseId == null || b.WarehouseId == warehouseId)
            .ToListAsync(ct);

        var locationIds = balances.Select(b => b.LocationId).Distinct();
        var locationInfos = await _locationQuery.GetLocationInfosAsync(locationIds, ct);

        decimal onHand = 0m;
        decimal eligibleOnHand = 0m;
        decimal blocked = 0m;

        foreach (var balance in balances)
        {
            onHand += balance.OnHand;
            if (locationInfos.TryGetValue(balance.LocationId, out var info))
            {
                bool isEligible = info.Type == LocationTypes.Storage && info.IsActive && !info.IsBlocked;
                if (isEligible)
                {
                    eligibleOnHand += balance.OnHand;
                }
                else if (info.Type == LocationTypes.Quarantine || info.Type == LocationTypes.Returns || info.IsBlocked)
                {
                    blocked += balance.OnHand;
                }
            }
        }

        // No reservation entity yet (A3): unpicked_reserved = 0.
        decimal reserved = 0m;
        decimal available = eligibleOnHand - reserved;

        // In-transit: approved transfers not yet received at this warehouse.
        decimal inTransit = await _inventory.Transfers
            .Where(t => t.DestWarehouseId == warehouseId
                && t.ProductId == productId
                && (t.Status == Transfer.Statuses.Approved
                    || t.Status == Transfer.Statuses.InTransit
                    || t.Status == Transfer.Statuses.Received))
            .SumAsync(t => t.ShippedQty - t.ReceivedQty, ct);

        // Incoming: open PO qty not yet received (planning signal, not a stock bucket).
        var incomingQuery = _inventory.IncomingProjections
            .Where(p => p.ProductId == productId);
        if (warehouseId != null)
        {
            incomingQuery = incomingQuery.Where(p => p.WarehouseId == warehouseId);
        }

        var incomingList = await incomingQuery.ToListAsync(ct);
        decimal incomingQty = incomingList.Sum(p => p.ExpectedQty);
        DateTimeOffset? incomingDate = incomingList.Count > 0
            ? incomingList.Min(p => p.ExpectedDate)
            : null;

        return new AvailabilitySnapshot(
            available,
            blocked,
            incomingQty,
            onHand,
            inTransit,
            incomingDate);
    }

    public record AvailabilitySnapshot(
        decimal Available,
        decimal Blocked,
        decimal IncomingQty,
        decimal OnHand,
        decimal InTransit,
        DateTimeOffset? IncomingDate);
}
