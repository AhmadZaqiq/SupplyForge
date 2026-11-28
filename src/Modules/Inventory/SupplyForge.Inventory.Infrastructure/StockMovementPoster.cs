using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.MasterData.Contracts.Location;
using SupplyForge.MasterData.Contracts.Warehouse;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.Shared.Idempotency;

namespace SupplyForge.Inventory.Infrastructure;

public sealed class StockMovementPoster
{
    private readonly InventoryDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly IIdempotencyStore _idempotency;
    private readonly IWarehouseBranchResolver _warehouseBranchResolver;
    private readonly ILocationQuery _locationQuery;
    private readonly IBranchGuard _branchGuard;

    public StockMovementPoster(
        InventoryDbContext db,
        ICurrentUser currentUser,
        ICorrelationIdProvider correlationIdProvider,
        IIdempotencyStore idempotency,
        IWarehouseBranchResolver warehouseBranchResolver,
        ILocationQuery locationQuery,
        IBranchGuard branchGuard)
    {
        _db = db;
        _currentUser = currentUser;
        _correlationIdProvider = correlationIdProvider;
        _idempotency = idempotency;
        _warehouseBranchResolver = warehouseBranchResolver;
        _locationQuery = locationQuery;
        _branchGuard = branchGuard;
    }

    public async Task PostAsync(StockMovement movement, string idempotencyKey, string payloadHash, CancellationToken ct = default)
    {
        Guid? warehouseBranchId = await _warehouseBranchResolver.GetBranchIdAsync(movement.WarehouseId, ct);
        if (warehouseBranchId is null)
        {
            throw new BranchNotFoundException($"Warehouse {movement.WarehouseId} not found (BRN-012).");
        }
        _branchGuard.RequireAccess(warehouseBranchId.Value);

        movement.BranchId = warehouseBranchId.Value;
        movement.CreatedByUserId = _currentUser.UserId;
        movement.CorrelationId = _correlationIdProvider.GetCorrelationId();

        // INV-091: Reject Returns-location inbound movements without an authorized
        // source document and reason. In P1 no customer-return workflow exists
        // (INV-090) and no Adjustment→Returns path (TD-7), so all inbound to a
        // Returns location is rejected.
        if (movement.Qty > 0m)
        {
            var locationInfos = await _locationQuery.GetLocationInfosAsync(
                new[] { movement.LocationId }, ct);
            if (locationInfos.TryGetValue(movement.LocationId, out var info)
                && info.Type == LocationTypes.Returns)
            {
                throw new InvalidOperationException(
                    "Returns-location inbound movements require an authorized source document and reason (INV-091).");
            }
        }

        IdempotencyClaimResult claim = await _idempotency.TryClaimAsync(idempotencyKey, payloadHash, ct);
        if (claim.Status == IdempotencyClaimStatus.AlreadyCompleted)
        {
            return;
        }
        if (claim.Status == IdempotencyClaimStatus.PayloadMismatch)
        {
            throw new InvalidOperationException("Idempotency payload mismatch (IDEM-04).");
        }
        if (claim.Status == IdempotencyClaimStatus.InProgress)
        {
            throw new InvalidOperationException("Idempotency key in progress (IDEM-05).");
        }

        Guid movementId = movement.MovementId;
        var balanceKey = (movement.ProductId, movement.WarehouseId, movement.LocationId);

        const int MaxRetries = 10;
        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                try
                {
                    await PostInternalAsync(movement, ct);
                    await _idempotency.CompleteAsync(idempotencyKey, null, ct);
                    return;
                }
                catch
                {
                    await _idempotency.ReleaseAsync(idempotencyKey, ct);
                    throw;
                }
            }
            catch (DbUpdateException ex) when (IsPrimaryKeyViolation(ex) && attempt < MaxRetries - 1)
            {
                await DetachFailedEntitiesAsync(movementId, balanceKey);
                await Task.Delay(Random.Shared.Next(10, 50), ct);
                continue;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxRetries - 1)
            {
                await DetachFailedEntitiesAsync(movementId, balanceKey);
                await Task.Delay(Random.Shared.Next(10, 50), ct);
                continue;
            }
        }

        await PostInternalAsync(movement, ct);
        await _idempotency.CompleteAsync(idempotencyKey, null, ct);
    }

    private static string GetCorrelationId()
    {
        // The CorrelationAndProblemDetailsMiddleware stores the correlation id in
        // HttpContext.Items["CorrelationId"]. In infrastructure code without direct
        // HttpContext access, fall back to the idempotency key so every posted
        // movement carries a traceable correlation identifier.
        return System.Diagnostics.Activity.Current?.Id
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_CORRELATION_ID")
            ?? Guid.NewGuid().ToString("N");
    }

    private async Task DetachFailedEntitiesAsync(Guid movementId, (Guid ProductId, Guid WarehouseId, Guid LocationId) balanceKey)
    {
        var movementEntry = _db.ChangeTracker.Entries<StockMovement>()
            .FirstOrDefault(e => e.Entity.MovementId == movementId);
        if (movementEntry != null)
        {
            movementEntry.State = EntityState.Detached;
        }

        var balanceEntry = _db.ChangeTracker.Entries<StockBalance>()
            .FirstOrDefault(e => e.Entity.ProductId == balanceKey.ProductId
                              && e.Entity.WarehouseId == balanceKey.WarehouseId
                              && e.Entity.LocationId == balanceKey.LocationId);
        if (balanceEntry != null)
        {
            balanceEntry.State = EntityState.Detached;
        }

        var costEntry = _db.ChangeTracker.Entries<ProductCost>()
            .FirstOrDefault(e => e.Entity.ProductId == balanceKey.ProductId
                              && e.Entity.WarehouseId == balanceKey.WarehouseId);
        if (costEntry != null)
        {
            costEntry.State = EntityState.Detached;
        }
    }

    private async Task PostInternalAsync(StockMovement movement, CancellationToken ct)
    {
        StockBalance? existing = await _db.StockBalances
            .FromSqlInterpolated($"SELECT * FROM inventory.StockBalance WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE ProductId = {movement.ProductId} AND WarehouseId = {movement.WarehouseId} AND LocationId = {movement.LocationId}")
            .AsTracking()
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            if (movement.Qty < 0)
            {
                throw new InvalidOperationException("Negative stock not allowed (INV-NEG).");
            }
            StockBalance balance = new StockBalance
            {
                ProductId = movement.ProductId,
                WarehouseId = movement.WarehouseId,
                LocationId = movement.LocationId,
                OnHand = movement.Qty,
                CreatedAt = movement.CreatedAt,
                UpdatedAt = movement.CreatedAt,
            };
            _db.StockBalances.Add(balance);
        }
        else
        {
            decimal newOnHand = existing.OnHand + movement.Qty;
            if (newOnHand < 0)
            {
                throw new InvalidOperationException("Negative stock not allowed (INV-NEG).");
            }
            existing.OnHand = newOnHand;
            existing.UpdatedAt = movement.CreatedAt;
        }

        ProductCost productCost = await GetOrCreateProductCostAsync(movement, ct);
        ApplyWacLogic(movement, productCost);
        productCost.UpdatedAt = movement.CreatedAt;

        _db.StockMovements.Add(movement);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<ProductCost> GetOrCreateProductCostAsync(StockMovement movement, CancellationToken ct)
    {
        // Pessimistic lock on ProductCost row (UPDLOCK, ROWLOCK, HOLDLOCK) via raw SQL.
        // Serializes concurrent WAC updates for the same (ProductId, WarehouseId) bucket,
        // even when movements originate from different locations.
        ProductCost? productCost = await _db.ProductCosts
            .FromSqlInterpolated($"SELECT * FROM inventory.ProductCost WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE ProductId = {movement.ProductId} AND WarehouseId = {movement.WarehouseId}")
            .AsTracking()
            .FirstOrDefaultAsync(ct);

        if (productCost is null)
        {
            if (movement.MovementType == StockMovement.MovementTypes.Issue
                || movement.MovementType == StockMovement.MovementTypes.TransferOut
                || movement.MovementType == StockMovement.MovementTypes.AdjustmentOut)
            {
                throw new InvalidOperationException($"No ProductCost exists for ProductId {movement.ProductId} and WarehouseId {movement.WarehouseId}; cannot issue/transfer out (WAC-04, WAC-08).");
            }

            productCost = new ProductCost
            {
                ProductId = movement.ProductId,
                WarehouseId = movement.WarehouseId,
                AvgCost = 0m,
                QtyOnHand = 0m,
                CreatedAt = movement.CreatedAt,
                UpdatedAt = movement.CreatedAt,
            };
            _db.ProductCosts.Add(productCost);
        }

        return productCost;
    }

    private void ApplyWacLogic(StockMovement movement, ProductCost productCost)
    {
        decimal absQty = Math.Abs(movement.Qty);

        switch (movement.MovementType)
        {
            case StockMovement.MovementTypes.Receipt:
                productCost.ApplyReceipt(absQty, movement.UnitCost);
                break;

            case StockMovement.MovementTypes.OpeningBalance:
                productCost.ApplyOpeningBalance(absQty, movement.UnitCost);
                break;

            case StockMovement.MovementTypes.AdjustmentIn:
                productCost.ApplyReceipt(absQty, movement.UnitCost);
                break;

            case StockMovement.MovementTypes.TransferIn:
                productCost.ApplyTransferIn(absQty, movement.UnitCost);
                break;

            case StockMovement.MovementTypes.Issue:
            case StockMovement.MovementTypes.AdjustmentOut:
                decimal costUsed = productCost.ApplyIssue(absQty);
                movement.UnitCost = costUsed;
                break;

            case StockMovement.MovementTypes.TransferOut:
                decimal transferCostUsed = productCost.ApplyTransferOut(absQty);
                movement.UnitCost = transferCostUsed;
                break;

            default:
                throw new InvalidOperationException($"Unknown movement type: {movement.MovementType}");
        }
    }

    private static bool IsPrimaryKeyViolation(DbUpdateException ex)
    {
        if (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            return sqlEx.Number == 2627 || sqlEx.Number == 2601;
        }
        return false;
    }
}