using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.Inventory.Infrastructure;
using SupplyForge.MasterData.Contracts.Warehouse;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.Shared.Data;

namespace SupplyForge.Inventory.Infrastructure;

/// <summary>
/// Orchestrates the Transfer document lifecycle (INV-030, INV-031, INV-032, BRN-020–023).
/// Coordinates document persistence and movement posting via StockMovementPoster within
/// ambient sessions. Same-warehouse moves post both legs atomically; inter-warehouse
/// splits shipping and receiving into separate ambient transactions per leg/receipt.
/// </summary>
public sealed class TransferOrchestrator
{
    private readonly InventoryDbContext _inventory;
    private readonly StockMovementPoster _poster;
    private readonly IWarehouseBranchResolver _warehouseBranchResolver;
    private readonly IBranchGuard _branchGuard;
    private readonly IDbSessionFactory _sessionFactory;

    public TransferOrchestrator(
        InventoryDbContext inventory,
        StockMovementPoster poster,
        IWarehouseBranchResolver warehouseBranchResolver,
        IBranchGuard branchGuard,
        IDbSessionFactory sessionFactory)
    {
        _inventory = inventory;
        _poster = poster;
        _warehouseBranchResolver = warehouseBranchResolver;
        _branchGuard = branchGuard;
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// Creates a Transfer in Draft status. Validates branch access for both warehouses.
    /// Inter-branch transfers are rejected (BRN-021 fail closed until B1).
    /// </summary>
    public async Task<Guid> CreateTransferAsync(
        Guid sourceWarehouseId,
        Guid sourceLocationId,
        Guid destWarehouseId,
        Guid destLocationId,
        Guid productId,
        decimal qty,
        CancellationToken ct = default)
    {
        if (qty <= 0m)
        {
            throw new ArgumentException("Transfer quantity must be positive.", nameof(qty));
        }

        Guid? sourceBranchId = await _warehouseBranchResolver.GetBranchIdAsync(sourceWarehouseId, ct);
        Guid? destBranchId = await _warehouseBranchResolver.GetBranchIdAsync(destWarehouseId, ct);

        if (sourceBranchId is null)
        {
            throw new InvalidOperationException("Source warehouse not found.");
        }
        if (destBranchId is null)
        {
            throw new InvalidOperationException("Destination warehouse not found.");
        }

        _branchGuard.RequireAccess(sourceBranchId.Value);
        _branchGuard.RequireAccess(destBranchId.Value);

        // BRN-021: inter-branch transfers fail closed until approval routing (B1)
        if (sourceBranchId.Value != destBranchId.Value)
        {
            throw new InvalidOperationException("Inter-branch transfers are rejected until BRN-021 approval routing (B1).");
        }

        var transfer = new Transfer
        {
            TransferId = Guid.NewGuid(),
            SourceWarehouseId = sourceWarehouseId,
            SourceLocationId = sourceLocationId,
            DestWarehouseId = destWarehouseId,
            DestLocationId = destLocationId,
            ProductId = productId,
            Qty = qty,
            ShippedQty = 0m,
            ReceivedQty = 0m,
            Status = Transfer.Statuses.Draft,
            BranchId = sourceBranchId.Value,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _inventory.Transfers.Add(transfer);
        await _inventory.SaveChangesAsync(ct);

        return transfer.TransferId;
    }

    /// <summary>
    /// Approves a Draft transfer (Draft → Approved). No movements posted.
    /// </summary>
    public async Task ApproveTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        Transfer? transfer = await _inventory.Transfers.FindAsync([transferId], ct);
        if (transfer is null)
        {
            throw new InvalidOperationException("Transfer not found.");
        }
        _branchGuard.RequireAccess(transfer.BranchId);
        if (transfer.Status != Transfer.Statuses.Draft)
        {
            throw new InvalidOperationException("Only Draft transfers can be approved.");
        }

        transfer.Status = Transfer.Statuses.Approved;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ships an Approved transfer.
    /// Same-warehouse: TransferOut + TransferIn posted atomically → Closed.
    /// Inter-warehouse: TransferOut posted → In Transit.
    /// </summary>
    public async Task ShipTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        Transfer? transfer = await _inventory.Transfers.FindAsync([transferId], ct);
        if (transfer is null)
        {
            throw new InvalidOperationException("Transfer not found.");
        }
        if (transfer.Status != Transfer.Statuses.Approved)
        {
            throw new InvalidOperationException("Only Approved transfers can be shipped.");
        }

        if (transfer.SourceWarehouseId == transfer.DestWarehouseId)
        {
            await ShipSameWarehouseAsync(transfer, ct);
        }
        else
        {
            await ShipInterWarehouseAsync(transfer, ct);
        }
    }

    private async Task ShipSameWarehouseAsync(Transfer transfer, CancellationToken ct)
    {
        await using IDbSession session = await _sessionFactory.BeginAsync();

        // TransferOut at source WAC
        StockMovement transferOut = new StockMovement
        {
            MovementId = Guid.NewGuid(),
            MovementType = StockMovement.MovementTypes.TransferOut,
            ProductId = transfer.ProductId,
            WarehouseId = transfer.SourceWarehouseId,
            LocationId = transfer.SourceLocationId,
            Qty = -transfer.Qty,
            UnitCost = 0m,
            SourceDocumentType = "Transfer",
            SourceDocumentId = transfer.TransferId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string shipKey = $"transfer:{transfer.TransferId}:ship";
        string shipHash = ComputePayloadHash(transferOut);
        await _poster.PostAsync(transferOut, shipKey, shipHash, ct);

        // Source WAC is mutated onto transferOut.UnitCost by the poster (ApplyWacLogic)
        decimal sourceWac = transferOut.UnitCost;

        // TransferIn at source WAC, same warehouse
        StockMovement transferIn = new StockMovement
        {
            MovementId = Guid.NewGuid(),
            MovementType = StockMovement.MovementTypes.TransferIn,
            ProductId = transfer.ProductId,
            WarehouseId = transfer.DestWarehouseId,
            LocationId = transfer.DestLocationId,
            Qty = transfer.Qty,
            UnitCost = sourceWac,
            SourceDocumentType = "Transfer",
            SourceDocumentId = transfer.TransferId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string receiveKey = $"transfer:{transfer.TransferId}:receive:{transfer.TransferId}";
        string receiveHash = ComputePayloadHash(transferIn);
        await _poster.PostAsync(transferIn, receiveKey, receiveHash, ct);

        // Both legs posted atomically
        transfer.ShippedQty = transfer.Qty;
        transfer.ReceivedQty = transfer.Qty;
        transfer.Status = Transfer.Statuses.Closed;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);

        await session.CommitAsync(ct);
    }

    private async Task ShipInterWarehouseAsync(Transfer transfer, CancellationToken ct)
    {
        await using IDbSession session = await _sessionFactory.BeginAsync();

        StockMovement transferOut = new StockMovement
        {
            MovementId = Guid.NewGuid(),
            MovementType = StockMovement.MovementTypes.TransferOut,
            ProductId = transfer.ProductId,
            WarehouseId = transfer.SourceWarehouseId,
            LocationId = transfer.SourceLocationId,
            Qty = -transfer.Qty,
            UnitCost = 0m,
            SourceDocumentType = "Transfer",
            SourceDocumentId = transfer.TransferId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string shipKey = $"transfer:{transfer.TransferId}:ship";
        string shipHash = ComputePayloadHash(transferOut);
        await _poster.PostAsync(transferOut, shipKey, shipHash, ct);

        transfer.ShippedQty = transfer.Qty;
        transfer.Status = Transfer.Statuses.InTransit;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);

        await session.CommitAsync(ct);
    }

    /// <summary>
    /// Receives a partial or full qty at the destination (inter-warehouse).
    /// Locks the Transfer row with HOLDLOCK, validates no over-receipt, skips replayed
    /// receives via movement existence pre-check, posts TransferIn at source WAC,
    /// increments ReceivedQty, advances status to Received / Closed.
    /// </summary>
    public async Task ReceiveTransferAsync(
        Guid transferId,
        Guid receiveId,
        decimal requestedQty,
        CancellationToken ct = default)
    {
        if (requestedQty <= 0m)
        {
            throw new ArgumentException("Receive quantity must be positive.", nameof(requestedQty));
        }

        await using IDbSession session = await _sessionFactory.BeginAsync();

        // Lock the Transfer row to serialize concurrent receives for the same transfer
        Transfer? transfer = await _inventory.Transfers
            .FromSqlInterpolated($"SELECT * FROM inventory.Transfer WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TransferId = {transferId}")
            .AsTracking()
            .FirstOrDefaultAsync(ct);

        if (transfer is null)
        {
            throw new InvalidOperationException("Transfer not found.");
        }

        // Pre-check: if a TransferIn movement with this SourceDocumentId already exists,
        // this is a replay — skip posting, skip ReceivedQty increment, just commit.
        bool alreadyPosted = await _inventory.StockMovements
            .AnyAsync(m => m.SourceDocumentId == receiveId, ct);

        if (alreadyPosted)
        {
            await session.CommitAsync(ct);
            return;
        }

        // Not a replay: validate status and quantity
        if (transfer.Status != Transfer.Statuses.InTransit && transfer.Status != Transfer.Statuses.Received)
        {
            throw new InvalidOperationException("Transfer must be In Transit or Received to receive.");
        }

        decimal remainingQty = transfer.ShippedQty - transfer.ReceivedQty;
        if (requestedQty > remainingQty)
        {
            throw new InvalidOperationException("Receive quantity exceeds remaining shipped quantity.");
        }

        // Source WAC from the original TransferOut movement
        StockMovement? transferOut = await _inventory.StockMovements
            .FirstOrDefaultAsync(
                m => m.SourceDocumentType == "Transfer"
                    && m.SourceDocumentId == transfer.TransferId
                    && m.MovementType == StockMovement.MovementTypes.TransferOut,
            ct);

        if (transferOut is null)
        {
            throw new InvalidOperationException("TransferOut movement not found.");
        }

        decimal sourceWac = transferOut.UnitCost;

        StockMovement transferIn = new StockMovement
        {
            MovementId = Guid.NewGuid(),
            MovementType = StockMovement.MovementTypes.TransferIn,
            ProductId = transfer.ProductId,
            WarehouseId = transfer.DestWarehouseId,
            LocationId = transfer.DestLocationId,
            Qty = requestedQty,
            UnitCost = sourceWac,
            SourceDocumentType = "Transfer",
            SourceDocumentId = receiveId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        string receiveKey = $"transfer:{transfer.TransferId}:receive:{receiveId}";
        string receiveHash = ComputePayloadHash(transferIn);
        await _poster.PostAsync(transferIn, receiveKey, receiveHash, ct);

        // Newly posted: increment cumulative received qty
        transfer.ReceivedQty += requestedQty;

        if (transfer.ReceivedQty >= transfer.ShippedQty)
        {
            transfer.Status = Transfer.Statuses.Closed;
        }
        else
        {
            transfer.Status = Transfer.Statuses.Received;
        }
        transfer.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);

        await session.CommitAsync(ct);
    }

    /// <summary>
    /// Cancels a Draft or Approved transfer. No movements are posted.
    /// </summary>
    public async Task CancelTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        Transfer? transfer = await _inventory.Transfers.FindAsync([transferId], ct);
        if (transfer is null)
        {
            throw new InvalidOperationException("Transfer not found.");
        }
        _branchGuard.RequireAccess(transfer.BranchId);
        if (transfer.Status != Transfer.Statuses.Draft && transfer.Status != Transfer.Statuses.Approved)
        {
            throw new InvalidOperationException("Only Draft or Approved transfers can be cancelled.");
        }

        transfer.Status = Transfer.Statuses.Cancelled;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Closes a transfer. Requires ReceivedQty == ShippedQty, or a posted
    /// transfer-shortfall adjustment (INV-032) to reconcile the difference.
    /// </summary>
    public async Task CloseTransferAsync(Guid transferId, CancellationToken ct = default)
    {
        Transfer? transfer = await _inventory.Transfers.FindAsync([transferId], ct);
        if (transfer is null)
        {
            throw new InvalidOperationException("Transfer not found.");
        }
        _branchGuard.RequireAccess(transfer.BranchId);
        if (transfer.Status == Transfer.Statuses.Cancelled || transfer.Status == Transfer.Statuses.Closed)
        {
            throw new InvalidOperationException("Cannot close a Cancelled or already-Closed transfer.");
        }
        if (transfer.Status != Transfer.Statuses.InTransit && transfer.Status != Transfer.Statuses.Received)
        {
            throw new InvalidOperationException("Only In Transit or Received transfers can be closed.");
        }

        bool fullyReceived = transfer.ReceivedQty >= transfer.ShippedQty;

        if (!fullyReceived)
        {
            bool hasShortfallAdjustment = await _inventory.Adjustments
                .AnyAsync(
                    a => a.SourceDocumentType == "Transfer"
                        && a.SourceDocumentId == transfer.TransferId
                        && a.Status == Adjustment.Statuses.Posted,
                    ct);

            if (!hasShortfallAdjustment)
            {
                throw new InvalidOperationException(
                    "Cannot close: transfer shortfall not reconciled. Create a posted transfer-shortfall adjustment first (INV-032).");
            }
        }

        transfer.Status = Transfer.Statuses.Closed;
        transfer.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);
    }

    private static string ComputePayloadHash(StockMovement movement)
    {
        string input = string.Format(
            "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
            movement.MovementType,
            movement.ProductId,
            movement.WarehouseId,
            movement.LocationId,
            movement.Qty,
            movement.UnitCost,
            movement.SourceDocumentType,
            movement.SourceDocumentId);

        byte[] bytes = Encoding.UTF8.GetBytes(input);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
