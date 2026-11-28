using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.MasterData.Contracts.Warehouse;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.Shared.Data;

namespace SupplyForge.Inventory.Infrastructure;

/// <summary>
/// Orchestrates the OpeningImport document lifecycle (INV-014).
/// Statuses: Draft → Validated (dry-run, no movements) → Posted (OpeningBalance movements) → Rejected.
/// Idempotency keys: opening:{OpeningImportId}:line:{LineIndex}.
/// </summary>
public sealed class OpeningImportOrchestrator
{
    private readonly InventoryDbContext _inventory;
    private readonly StockMovementPoster _poster;
    private readonly IWarehouseBranchResolver _warehouseBranchResolver;
    private readonly IBranchGuard _branchGuard;
    private readonly IDbSessionFactory _sessionFactory;

    public OpeningImportOrchestrator(
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
    /// Creates an OpeningImport in Draft status with the given lines.
    /// </summary>
    public async Task<Guid> CreateOpeningImportAsync(
        Guid branchId,
        IReadOnlyList<OpeningImportLineInput> lines,
        CancellationToken ct = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("At least one opening import line is required.", nameof(lines));
        }

        _branchGuard.RequireAccess(branchId);

        var import = new OpeningImport
        {
            OpeningImportId = Guid.NewGuid(),
            Status = OpeningImport.Statuses.Draft,
            BranchId = branchId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _inventory.OpeningImports.Add(import);

        int index = 0;
        foreach (OpeningImportLineInput line in lines)
        {
            var importLine = new OpeningImportLine
            {
                OpeningImportLineId = Guid.NewGuid(),
                OpeningImportId = import.OpeningImportId,
                ProductId = line.ProductId,
                WarehouseId = line.WarehouseId,
                LocationId = line.LocationId,
                Qty = line.Qty,
                UnitCost = line.UnitCost,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _inventory.OpeningImportLines.Add(importLine);
            index++;
        }

        await _inventory.SaveChangesAsync(ct);

        return import.OpeningImportId;
    }

    /// <summary>
    /// Dry-run validation (INV-014). Validates that quantities are positive, LocationId is
    /// present, and referenced warehouses exist and are accessible. Sets status to Validated.
    /// No StockMovements are posted.
    /// </summary>
    public async Task ValidateOpeningImportAsync(Guid openingImportId, CancellationToken ct = default)
    {
        OpeningImport? import = await _inventory.OpeningImports.FindAsync([openingImportId], ct);
        if (import is null)
        {
            throw new InvalidOperationException("Opening import not found.");
        }
        _branchGuard.RequireAccess(import.BranchId);
        if (import.Status != OpeningImport.Statuses.Draft)
        {
            throw new InvalidOperationException("Only Draft opening imports can be validated.");
        }

        List<OpeningImportLine> lines = await _inventory.OpeningImportLines
            .Where(l => l.OpeningImportId == openingImportId)
            .ToListAsync(ct);

        foreach (OpeningImportLine line in lines)
        {
            if (line.Qty <= 0m)
            {
                throw new InvalidOperationException($"Opening import line {line.OpeningImportLineId} has non-positive qty.");
            }
            if (line.LocationId == Guid.Empty)
            {
                throw new InvalidOperationException($"Opening import line {line.OpeningImportLineId} requires a LocationId.");
            }

            Guid? warehouseBranchId = await _warehouseBranchResolver.GetBranchIdAsync(line.WarehouseId, ct);
            if (warehouseBranchId is null)
            {
                throw new InvalidOperationException($"Warehouse {line.WarehouseId} not found.");
            }
            _branchGuard.RequireAccess(warehouseBranchId.Value);
        }

        import.Status = OpeningImport.Statuses.Validated;
        import.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Posts an Validated opening import (Validated → Posted).
    /// Creates OpeningBalance movements via the poster for each line.
    /// Idempotency key per line: opening:{OpeningImportId}:line:{LineIndex}.
    /// </summary>
    public async Task PostOpeningImportAsync(Guid openingImportId, CancellationToken ct = default)
    {
        OpeningImport? import = await _inventory.OpeningImports.FindAsync([openingImportId], ct);
        if (import is null)
        {
            throw new InvalidOperationException("Opening import not found.");
        }
        _branchGuard.RequireAccess(import.BranchId);
        if (import.Status != OpeningImport.Statuses.Validated)
        {
            throw new InvalidOperationException("Only Validated opening imports can be posted.");
        }

        await using IDbSession session = await _sessionFactory.BeginAsync();

        List<OpeningImportLine> lines = await _inventory.OpeningImportLines
            .Where(l => l.OpeningImportId == openingImportId)
            .OrderBy(l => l.CreatedAt)
            .ToListAsync(ct);

        int index = 0;
        foreach (OpeningImportLine line in lines)
        {
            StockMovement movement = new StockMovement
            {
                MovementId = Guid.NewGuid(),
                MovementType = StockMovement.MovementTypes.OpeningBalance,
                ProductId = line.ProductId,
                WarehouseId = line.WarehouseId,
                LocationId = line.LocationId,
                Qty = line.Qty,
                UnitCost = line.UnitCost,
                SourceDocumentType = "OpeningImport",
                SourceDocumentId = openingImportId,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            string idemKey = $"opening:{openingImportId}:line:{index}";
            string idemHash = ComputePayloadHash(movement);
            await _poster.PostAsync(movement, idemKey, idemHash, ct);

            index++;
        }

        import.Status = OpeningImport.Statuses.Posted;
        import.UpdatedAt = DateTimeOffset.UtcNow;

        await _inventory.SaveChangesAsync(ct);

        await session.CommitAsync(ct);
    }

    /// <summary>
    /// Rejects an opening import at any pre-posted state.
    /// </summary>
    public async Task RejectOpeningImportAsync(Guid openingImportId, CancellationToken ct = default)
    {
        OpeningImport? import = await _inventory.OpeningImports.FindAsync([openingImportId], ct);
        if (import is null)
        {
            throw new InvalidOperationException("Opening import not found.");
        }
        _branchGuard.RequireAccess(import.BranchId);
        if (import.Status == OpeningImport.Statuses.Posted || import.Status == OpeningImport.Statuses.Rejected)
        {
            throw new InvalidOperationException("Posted or Rejected opening imports cannot be rejected again.");
        }

        import.Status = OpeningImport.Statuses.Rejected;
        import.UpdatedAt = DateTimeOffset.UtcNow;

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
/// Input for a single opening import line (INV-014).
/// LocationId is required because the poster locks StockBalance by
/// (ProductId, WarehouseId, LocationId).
/// </summary>
public sealed record OpeningImportLineInput(
    Guid ProductId,
    Guid WarehouseId,
    Guid LocationId,
    decimal Qty,
    decimal UnitCost);
