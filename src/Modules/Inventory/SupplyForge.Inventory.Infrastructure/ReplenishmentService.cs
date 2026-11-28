using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.MasterData.Contracts.Location;
using SupplyForge.MasterData.Contracts.Warehouse;
using SupplyForge.Shared.Data;
using SupplyForge.Shared.Outbox;

namespace SupplyForge.Inventory.Infrastructure;

/// <summary>
/// Replenishment service (INV-060, INV-061).
/// Checks each product+warehouse reorder point: when Available + Incoming falls
/// below ReorderPointQty, marks the reorder point alerted and publishes
/// StockBelowReorderPoint via the outbox. Alerts once per breach until recovered
/// (IsAlerted reset when stock recovers above threshold).
/// </summary>
public sealed class ReplenishmentService
{
    private readonly InventoryDbContext _inventory;
    private readonly AvailabilityCalculator _calculator;
    private readonly IWarehouseBranchResolver _warehouseBranchResolver;
    private readonly IOutbox _outbox;
    private readonly TimeProvider _clock;
    private readonly IDbSessionFactory _sessionFactory;

    public ReplenishmentService(
        InventoryDbContext inventory,
        AvailabilityCalculator calculator,
        IWarehouseBranchResolver warehouseBranchResolver,
        IOutbox outbox,
        TimeProvider clock,
        IDbSessionFactory sessionFactory)
    {
        _inventory = inventory;
        _calculator = calculator;
        _warehouseBranchResolver = warehouseBranchResolver;
        _outbox = outbox;
        _clock = clock;
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// Evaluates all unalerted reorder points and publishes
    /// StockBelowReorderPoint for each breach. Runs inside one ambient
    /// transaction so IsAlerted and the outbox row commit together.
    /// </summary>
    public async Task CheckReorderPointsAsync(CancellationToken ct = default)
    {
        await using IDbSession session = await _sessionFactory.BeginAsync();

        var reorderPoints = await _inventory.ReorderPoints
            .FromSqlInterpolated($"SELECT * FROM inventory.ReorderPoint WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE IsAlerted = 0")
            .ToListAsync(ct);

        foreach (var rp in reorderPoints)
        {
            var availability = await _calculator.CalculateAsync(rp.ProductId, rp.WarehouseId, ct);
            if (availability.Available + availability.IncomingQty < rp.ReorderPointQty)
            {
                rp.IsAlerted = true;
                rp.UpdatedAt = _clock.GetUtcNow();

                var payload = JsonSerializer.Serialize(new
                {
                    ProductId = rp.ProductId,
                    WarehouseId = rp.WarehouseId,
                    Available = availability.Available,
                    IncomingQty = availability.IncomingQty,
                    ReorderPointQty = rp.ReorderPointQty,
                    BreachQty = rp.ReorderPointQty - (availability.Available + availability.IncomingQty)
                });

                await _outbox.EnqueueAsync(
                    "StockBelowReorderPoint",
                    payload,
                    1,
                    null,
                    ct);
            }
        }

        await _inventory.SaveChangesAsync(ct);
        await session.CommitAsync(ct);
    }

    /// <summary>
    /// Clears the alert flag when stock recovers above the reorder point
    /// (INV-061: alert once per breach until recovered).
    /// Runs inside one ambient session with HOLDLOCK to prevent double-alerting
    /// when two jobs run concurrently.
    /// </summary>
    public async Task ClearRecoveredAlertsAsync(CancellationToken ct = default)
    {
        await using IDbSession session = await _sessionFactory.BeginAsync();

        var alertedPoints = await _inventory.ReorderPoints
            .FromSqlInterpolated($"SELECT * FROM inventory.ReorderPoint WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE IsAlerted = 1")
            .ToListAsync(ct);

        foreach (var rp in alertedPoints)
        {
            var availability = await _calculator.CalculateAsync(rp.ProductId, rp.WarehouseId, ct);
            if (availability.Available + availability.IncomingQty >= rp.ReorderPointQty)
            {
                rp.IsAlerted = false;
                rp.UpdatedAt = _clock.GetUtcNow();
            }
        }

        await _inventory.SaveChangesAsync(ct);
        await session.CommitAsync(ct);
    }
}
