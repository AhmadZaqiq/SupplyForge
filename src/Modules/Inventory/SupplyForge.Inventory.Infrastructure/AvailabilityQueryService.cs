using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.Inventory.Contracts.Availability;
using SupplyForge.MasterData.Contracts.Location;
using SupplyForge.MasterData.Contracts.Warehouse;
using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.Inventory.Infrastructure;

/// <summary>
/// Availability query service (INV-100).
/// Returns all stock buckets per product+warehouse: on-hand, available,
/// reserved, blocked, in-transit, incoming qty and incoming date.
/// Cross-module reads location eligibility via ILocationQuery (MOD-2).
/// </summary>
public sealed class AvailabilityQueryService : IAvailabilityQueryService
{
    private readonly AvailabilityCalculator _calculator;
    private readonly IWarehouseBranchResolver _warehouseBranchResolver;
    private readonly IBranchGuard _branchGuard;

    public AvailabilityQueryService(
        AvailabilityCalculator calculator,
        IWarehouseBranchResolver warehouseBranchResolver,
        IBranchGuard branchGuard)
    {
        _calculator = calculator;
        _warehouseBranchResolver = warehouseBranchResolver;
        _branchGuard = branchGuard;
    }

    /// <summary>
    /// Returns availability for a product at a warehouse.
    /// Branch scope is enforced: the caller must have access to the warehouse's branch.
    /// </summary>
    public async Task<AvailabilityResultDto> GetAvailabilityAsync(
        Guid productId,
        Guid warehouseId,
        CancellationToken ct = default)
    {
        Guid? branchId = await _warehouseBranchResolver.GetBranchIdAsync(warehouseId, ct);
        if (branchId is null)
        {
            throw new InvalidOperationException("Warehouse not found.");
        }
        _branchGuard.RequireAccess(branchId.Value);

        var snapshot = await _calculator.CalculateAsync(productId, warehouseId, ct);

        return new AvailabilityResultDto(
            ProductId: productId,
            WarehouseId: warehouseId,
            OnHand: snapshot.OnHand,
            Available: snapshot.Available,
            Reserved: 0m, // No reservation entity yet (A3): unpicked_reserved = 0.
            Blocked: snapshot.Blocked,
            InTransit: snapshot.InTransit,
            IncomingQty: snapshot.IncomingQty,
            IncomingDate: snapshot.IncomingDate);
    }
}
