using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.Shared.Data;

namespace SupplyForge.Inventory.Infrastructure;

/// <summary>
/// Orchestrates the Adjustment document lifecycle (INV-040, INV-032, INV-042).
/// Two kinds of adjustment:
/// 1. Normal adjustment (no SourceDocumentType): posts AdjustmentIn/AdjustmentOut movements via poster.
/// 2. Transfer-shortfall reconciliation (SourceDocumentType = "Transfer", SourceDocumentId = TransferId):
///    audit-only, Status = Posted, creates no StockMovements.
/// Statuses: Draft → Approved → Posted → Rejected (reject at any pre-posted state).
/// INV-041 (threshold approval, creator cannot approve) is blocked on B1 and not implemented here.
/// </summary>
public sealed class AdjustmentOrchestrator
{
    private readonly InventoryDbContext _inventory;
    private readonly StockMovementPoster _poster;
    private readonly IBranchGuard _branchGuard;
    private readonly IDbSessionFactory _sessionFactory;

    public AdjustmentOrchestrator(
        InventoryDbContext inventory,
        StockMovementPoster poster,
        IBranchGuard branchGuard,
        IDbSessionFactory sessionFactory)
    {
        _inventory = inventory;
        _poster = poster;
        _branchGuard = branchGuard;
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// Creates a normal adjustment in Draft status with the given lines.
    /// Branch access is validated for the adjustment's BranchId.
    /// </summary>
    public async Task<Guid> CreateAdjustmentAsync(
        string reasonCode,
        string comment,
        Guid branchId,
        IReadOnlyList<AdjustmentLineInput> lines,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Reason code is required.", nameof(reasonCode));
        }
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one adjustment line is required.", nameof(lines));
        }

        _branchGuard.RequireAccess(branchId);

        // Validate adjustment line quantity/movement-type consistency
        // before persisting any document or line (INV-040 application boundary).
        int lineIndex = 0;
        foreach (AdjustmentLineInput line in lines)
        {
            if (line.Qty == 0m)
            {
                throw new InvalidOperationException(
                    $"Adjustment line {lineIndex} has zero quantity; zero movements are not permitted (INV-040).");
            }
            if (line.MovementType == StockMovement.MovementTypes.AdjustmentIn && line.Qty < 0m)
            {
                throw new InvalidOperationException(
                    $"Adjustment line {lineIndex} is AdjustmentIn with negative quantity {line.Qty}; AdjustmentIn requires positive quantity (INV-040).");
            }
            if (line.MovementType == StockMovement.MovementTypes.AdjustmentOut && line.Qty > 0m)
            {
                throw new InvalidOperationException(
                    $"Adjustment line {lineIndex} is AdjustmentOut with positive quantity {line.Qty}; AdjustmentOut requires negative quantity (INV-040).");
            }
            lineIndex++;
        }

        var adjustment = new Adjustment
        {
            AdjustmentId = Guid.NewGuid(),
            ReasonCode = reasonCode,
            Comment = comment,
            Status = Adjustment.Statuses.Draft,
            SourceDocumentType = null,
            SourceDocumentId = null,
            BranchId = branchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _inventory.Adjustments.Add(adjustment);

        int index = 0;
        foreach (AdjustmentLineInput line in lines)
        {
            var adjustmentLine = new AdjustmentLine
            {
                AdjustmentLineId = Guid.NewGuid(),
                AdjustmentId = adjustment.AdjustmentId,
                ProductId = line.ProductId,
                WarehouseId = line.WarehouseId,
                LocationId = line.LocationId,
                Qty = line.Qty,
                MovementType = line.MovementType,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _inventory.AdjustmentLines.Add(adjustmentLine);
            index++;
        }

        await _inventory.SaveChangesAsync(ct);

        return adjustment.AdjustmentId;
    }

    /// <summary>
    /// Creates a transfer-shortfall reconciliation adjustment (INV-032).
    /// Audit-only: Status = Posted, creates no StockMovements.
    /// Unlike normal adjustments (INV-040), transfer-shortfall adjustments bypass the
    /// Draft → Approved → Posted lifecycle because they create no StockMovements and
    /// serve purely as documentation of a transfer shortfall. Status is Posted directly
    /// since no movement posting requires the Approved → Posted transition.
    /// Duplicate prevention: rejects if a Transfer adjustment already exists for this TransferId.
    /// </summary>
    public async Task<Guid> CreateTransferShortfallAdjustmentAsync(
        Guid transferId,
        string reasonCode,
        string comment,
        Guid branchId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            throw new ArgumentException("Reason code is required.", nameof(reasonCode));
        }

        await using IDbSession session = await _sessionFactory.BeginAsync();

        // Lock the Transfer row to serialize concurrent shortfall adjustment creation
        Transfer? transfer = await _inventory.Transfers
            .FromSqlInterpolated($"SELECT * FROM inventory.Transfer WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE TransferId = {transferId}")
            .AsTracking()
            .FirstOrDefaultAsync(ct);

        if (transfer is null)
        {
            throw new InvalidOperationException("Transfer not found.");
        }

        if (transfer.Status != Transfer.Statuses.InTransit && transfer.Status != Transfer.Statuses.Received)
        {
            throw new InvalidOperationException("Only In Transit or Received transfers can be reconciled with a shortfall adjustment.");
        }

        if (transfer.ReceivedQty >= transfer.ShippedQty)
        {
            throw new InvalidOperationException("No shortfall exists for this transfer.");
        }

        // Branch ownership: the adjustment's branch must own the transfer
        _branchGuard.RequireAccess(branchId);
        if (branchId != transfer.BranchId)
        {
            throw new InvalidOperationException(
                "Branch scope rejected: adjustment branch does not own the transfer (BRN-012).");
        }

        // Duplicate prevention within the same ambient transaction
        bool duplicate = await _inventory.Adjustments
            .AnyAsync(a => a.SourceDocumentType == "Transfer" && a.SourceDocumentId == transferId, ct);
        if (duplicate)
        {
            throw new InvalidOperationException(
                "A transfer-shortfall adjustment already exists for this transfer (INV-032 duplicate prevention).");
        }

        var adjustment = new Adjustment
        {
            AdjustmentId = Guid.NewGuid(),
            ReasonCode = reasonCode,
            Comment = comment,
            Status = Adjustment.Statuses.Posted,
            SourceDocumentType = "Transfer",
            SourceDocumentId = transferId,
            BranchId = branchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _inventory.Adjustments.Add(adjustment);
        await _inventory.SaveChangesAsync(ct);

        await session.CommitAsync(ct);

        return adjustment.AdjustmentId;
    }

    /// <summary>
    /// Submits a Draft adjustment for approval (Draft → PendingApproval).
    /// </summary>
    public async Task SubmitForApprovalAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        Adjustment? adjustment = await _inventory.Adjustments.FindAsync([adjustmentId], ct);
        if (adjustment is null)
        {
            throw new InvalidOperationException("Adjustment not found.");
        }
        if (adjustment.Status != Adjustment.Statuses.Draft)
        {
            throw new InvalidOperationException("Only Draft adjustments can be submitted for approval.");
        }

        adjustment.Status = Adjustment.Statuses.PendingApproval;
        adjustment.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Approves a Draft or PendingApproval adjustment (Draft/PendingApproval → Approved).
    /// </summary>
    public async Task ApproveAdjustmentAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        Adjustment? adjustment = await _inventory.Adjustments.FindAsync([adjustmentId], ct);
        if (adjustment is null)
        {
            throw new InvalidOperationException("Adjustment not found.");
        }
        _branchGuard.RequireAccess(adjustment.BranchId);
        if (adjustment.Status != Adjustment.Statuses.Draft && adjustment.Status != Adjustment.Statuses.PendingApproval)
        {
            throw new InvalidOperationException("Only Draft or Pending Approval adjustments can be approved.");
        }

        adjustment.Status = Adjustment.Statuses.Approved;
        adjustment.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Posts an Approved adjustment (Approved → Posted).
    /// Normal adjustments post AdjustmentIn/AdjustmentOut movements via the poster
    /// (negative stock guard enforced). Transfer-shortfall adjustments are already Posted.
    /// </summary>
    public async Task PostAdjustmentAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        Adjustment? adjustment = await _inventory.Adjustments.FindAsync([adjustmentId], ct);
        if (adjustment is null)
        {
            throw new InvalidOperationException("Adjustment not found.");
        }
        _branchGuard.RequireAccess(adjustment.BranchId);

        // Transfer-shortfall adjustments are audit-only and already Posted
        if (adjustment.SourceDocumentType == "Transfer")
        {
            if (adjustment.Status != Adjustment.Statuses.Posted)
            {
                throw new InvalidOperationException("Transfer-shortfall adjustment must be in Posted status.");
            }
            return;
        }

        if (adjustment.Status != Adjustment.Statuses.Approved)
        {
            throw new InvalidOperationException("Only Approved adjustments can be posted.");
        }

        await using IDbSession session = await _sessionFactory.BeginAsync();

        List<AdjustmentLine> lines = await _inventory.AdjustmentLines
            .Where(l => l.AdjustmentId == adjustmentId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        int index = 0;
        foreach (AdjustmentLine line in lines)
        {
            StockMovement movement = new StockMovement
            {
                MovementId = Guid.NewGuid(),
                MovementType = line.MovementType,
                ProductId = line.ProductId,
                WarehouseId = line.WarehouseId,
                LocationId = line.LocationId,
                Qty = line.Qty,
                UnitCost = 0m,
                SourceDocumentType = "Adjustment",
                SourceDocumentId = adjustmentId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            string idemKey = $"adjustment:{adjustmentId}:line:{index}";
            string idemHash = ComputePayloadHash(movement);
            await _poster.PostAsync(movement, idemKey, idemHash, ct);

            index++;
        }

        adjustment.Status = Adjustment.Statuses.Posted;
        adjustment.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);

        await session.CommitAsync(ct);
    }

    /// <summary>
    /// Rejects an adjustment at any pre-posted state (Draft, PendingApproval, Approved → Rejected).
    /// </summary>
    public async Task RejectAdjustmentAsync(Guid adjustmentId, CancellationToken ct = default)
    {
        Adjustment? adjustment = await _inventory.Adjustments.FindAsync([adjustmentId], ct);
        if (adjustment is null)
        {
            throw new InvalidOperationException("Adjustment not found.");
        }
        _branchGuard.RequireAccess(adjustment.BranchId);
        if (adjustment.Status == Adjustment.Statuses.Posted || adjustment.Status == Adjustment.Statuses.Rejected)
        {
            throw new InvalidOperationException("Posted or Rejected adjustments cannot be rejected again.");
        }

        adjustment.Status = Adjustment.Statuses.Rejected;
        adjustment.UpdatedAt = DateTimeOffset.UtcNow;

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

/// <summary>
/// Input for a single adjustment line (INV-040).
/// Qty positive for AdjustmentIn, negative for AdjustmentOut.
/// </summary>
public sealed record AdjustmentLineInput(
    Guid ProductId,
    Guid WarehouseId,
    Guid LocationId,
    decimal Qty,
    string MovementType);
