using System.Data;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SupplyForge.Inventory.Domain;
using SupplyForge.Inventory.Infrastructure;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Security;
using SupplyForge.PlatformCore.Infrastructure.Services;
using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Data;
using SupplyForge.Shared.Idempotency;
using SupplyForge.Host.Data;
using Xunit;

namespace SupplyForge.Inventory.IntegrationTests;

/// <summary>
/// Integration tests for Adjustment orchestration (A1-7, INV-040, INV-042, INV-032).
/// Tests run against throwaway SQL Server databases with real EF Core migrations.
/// Requirement IDs are included in every test name (NFR-051).
/// </summary>
public sealed class AdjustmentOrchestrationTests : IAsyncLifetime
{
    private string _dbName = string.Empty;
    private IServiceProvider _services = null!;

    private Guid _productId;
    private Guid _warehouse1Id;
    private Guid _warehouse2Id;
    private Guid _location1Id;
    private Guid _location2Id;
    private Guid _branch1Id;

    public async Task InitializeAsync()
    {
        _dbName = ThrowawayDatabase.NewName("InvAdj");
        await ThrowawayDatabase.CreateAsync(_dbName);

        await using (var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(_dbName), SystemBranchScope.Create()))
        {
            await md.Database.MigrateAsync();
        }
        await using (var pc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(_dbName)))
        {
            await pc.Database.MigrateAsync();
        }
        await using (var inv = new InventoryDbContext(ThrowawayDatabase.Inventory(_dbName), SystemBranchScope.Create()))
        {
            await inv.Database.MigrateAsync();
        }

        await SeedMasterDataAsync();

        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddSingleton(TimeProvider.System);
        sc.AddScoped<BranchScopeHolder>();
        sc.AddScoped<ICurrentBranchScope>(sp => sp.GetRequiredService<BranchScopeHolder>().Scope);
        sc.AddScoped<IBranchScopeProvider, BranchScopeProvider>();
        sc.AddScoped<PlatformSharedServices>();
        sc.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<PlatformSharedServices>());
        sc.AddScoped<IIdempotencyStore>(sp => sp.GetRequiredService<PlatformSharedServices>());
        sc.AddScoped<SupplyForge.Shared.Sequencing.IDocumentSequenceService>(sp => sp.GetRequiredService<PlatformSharedServices>());
        sc.AddScoped<SupplyForge.Shared.Notifications.INotificationStore>(sp => sp.GetRequiredService<PlatformSharedServices>());
        sc.AddScoped<SupplyForge.Shared.Jobs.IJobRunStore>(sp => sp.GetRequiredService<PlatformSharedServices>());
        sc.AddScoped<SupplyForge.MasterData.Contracts.Warehouse.IWarehouseBranchResolver,
            SupplyForge.MasterData.Infrastructure.Warehouse.WarehouseBranchResolver>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.StockMovementPoster>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.TransferOrchestrator>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.AdjustmentOrchestrator>();

        sc.AddDbContext<PlatformCoreDbContext>(o => o.UseSqlServer(ThrowawayDatabase.For(_dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema)));
        sc.AddDbContext<MasterDataDbContext>(o => o.UseSqlServer(ThrowawayDatabase.For(_dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema)));
        sc.AddDbContext<InventoryDbContext>(o => o.UseSqlServer(ThrowawayDatabase.For(_dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema)));
        sc.AddDbContext<SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext>(o => o.UseSqlServer(ThrowawayDatabase.For(_dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext.Schema)));

        sc.AddScoped<IDbSessionFactory>(sp => new AmbientDbSessionFactory(
            sp.GetRequiredService<PlatformCoreDbContext>(),
            sp.GetRequiredService<SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext>(),
            sp.GetRequiredService<MasterDataDbContext>(),
            sp.GetRequiredService<InventoryDbContext>()));

        _services = sc.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        (_services as IDisposable)?.Dispose();
        await ThrowawayDatabase.DropAsync(_dbName);
    }

    private async Task SeedMasterDataAsync()
    {
        await using var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(_dbName), SystemBranchScope.Create());
        await using var pc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(_dbName));
        await using var inv = new InventoryDbContext(ThrowawayDatabase.Inventory(_dbName), SystemBranchScope.Create());

        var company = new Company
        {
            Id = Company.SingletonId,
            LegalName = "Test Company",
            BaseCurrency = "USD",
            Timezone = "UTC",
            DefaultLanguage = "en",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        md.Companies.Add(company);

        var branch1 = new Branch { Id = Guid.NewGuid(), Code = "BR1", Name = "Branch 1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var branch2 = new Branch { Id = Guid.NewGuid(), Code = "BR2", Name = "Branch 2", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.Branches.AddRange(branch1, branch2);

        var uomId = Guid.NewGuid();
        var uom = new UnitOfMeasure { Id = uomId, Code = "EA", Name = "Each", CreatedAt = DateTimeOffset.UtcNow };
        md.UnitsOfMeasure.Add(uom);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "PROD-001",
            Name = "Test Product",
            Status = "Active",
            BaseUnitOfMeasureId = uomId,
            CategoryId = null,
            CreatedAt = DateTimeOffset.UtcNow
        };
        md.Products.Add(product);

        var warehouse1 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-001", Name = "Warehouse 1", BranchId = branch1.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var warehouse2 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-002", Name = "Warehouse 2", BranchId = branch1.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.Warehouses.AddRange(warehouse1, warehouse2);

        var location1 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-001", Name = "Location 1", Type = "Storage", WarehouseId = warehouse1.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var location2 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-002", Name = "Location 2", Type = "Storage", WarehouseId = warehouse2.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.StorageLocations.AddRange(location1, location2);

        await md.SaveChangesAsync();

        _productId = product.Id;
        _warehouse1Id = warehouse1.Id;
        _warehouse2Id = warehouse2.Id;
        _location1Id = location1.Id;
        _location2Id = location2.Id;
        _branch1Id = branch1.Id;
    }

    private (IServiceScope Scope, AdjustmentOrchestrator Orchestrator, InventoryDbContext InventoryDb, BranchScopeHolder Holder) CreateSystemScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = SystemBranchScope.Create();
        var orchestrator = scope.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, orchestrator, inventoryDb, holder);
    }

    private static AdjustmentLineInput LineIn(Guid productId, Guid warehouseId, Guid locationId, decimal qty)
    {
        return new AdjustmentLineInput(productId, warehouseId, locationId, qty, StockMovement.MovementTypes.AdjustmentIn);
    }

    private static AdjustmentLineInput LineOut(Guid productId, Guid warehouseId, Guid locationId, decimal qty)
    {
        return new AdjustmentLineInput(productId, warehouseId, locationId, -qty, StockMovement.MovementTypes.AdjustmentOut);
    }

    [Fact]
    public async Task INV_040_Adjustment_PostsMovements()
    {
        // Arrange - opening stock
        var (initScope, _, initInventoryDb, _) = CreateSystemScope();
        try
        {
            using var posterScope = _services.CreateScope();
            var posterHolder = posterScope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
            posterHolder.Scope = SystemBranchScope.Create();
            var poster = posterScope.ServiceProvider.GetRequiredService<StockMovementPoster>();
            var opening = new StockMovement
            {
                MovementId = Guid.NewGuid(),
                MovementType = StockMovement.MovementTypes.OpeningBalance,
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _location1Id,
                Qty = 100m,
                UnitCost = 10.00m,
                SourceDocumentType = "Test",
                SourceDocumentId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await poster.PostAsync(opening, "inv-040-init", "hash-inv-040-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - create, approve, post adjustment
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found 10 units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 10m) });
            await orchestrator.ApproveAdjustmentAsync(adjustmentId);
            await orchestrator.PostAdjustmentAsync(adjustmentId);

            // Assert - adjustment is Posted
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Posted, adjustment.Status);

            // Assert - AdjustmentIn movement posted
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == adjustmentId && m.MovementType == StockMovement.MovementTypes.AdjustmentIn);
            Assert.NotNull(movement);
            Assert.Equal(10m, movement.Qty);
            Assert.Equal(_warehouse1Id, movement.WarehouseId);
            Assert.Equal(_location1Id, movement.LocationId);

            // Assert - stock increased
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(110m, productCost.QtyOnHand);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_NeverBelowZero()
    {
        // Arrange - opening stock 10 units
        var (initScope, _, initInventoryDb, _) = CreateSystemScope();
        try
        {
            using var posterScope = _services.CreateScope();
            var posterHolder = posterScope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
            posterHolder.Scope = SystemBranchScope.Create();
            var poster = posterScope.ServiceProvider.GetRequiredService<StockMovementPoster>();
            var opening = new StockMovement
            {
                MovementId = Guid.NewGuid(),
                MovementType = StockMovement.MovementTypes.OpeningBalance,
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _location1Id,
                Qty = 10m,
                UnitCost = 10.00m,
                SourceDocumentType = "Test",
                SourceDocumentId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await poster.PostAsync(opening, "inv-040-neg-init", "hash-inv-040-neg-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act & Assert - adjustment out of 20 units exceeds stock, rejected by poster
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "DAMAGED", "Damaged 20 units", _branch1Id,
                new[] { LineOut(_productId, _warehouse1Id, _location1Id, 20m) });
            await orchestrator.ApproveAdjustmentAsync(adjustmentId);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.PostAdjustmentAsync(adjustmentId));

            // Assert - adjustment not Posted
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Approved, adjustment.Status);

            // Assert - stock unchanged
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(10m, productCost.QtyOnHand);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_StatusTransitions()
    {
        // Arrange - create Draft adjustment
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 5m) });

            // Assert - Draft
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Draft, adjustment.Status);

            // Act - Approve
            await orchestrator.ApproveAdjustmentAsync(adjustmentId);
            adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Approved, adjustment.Status);

            // Act - Reject from Approved
            await orchestrator.RejectAdjustmentAsync(adjustmentId);
            adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Rejected, adjustment.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_CannotPost_FromDraft()
    {
        // Arrange - Draft adjustment
        var (scope, orchestrator, _, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 5m) });

            // Act & Assert - cannot post from Draft
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.PostAdjustmentAsync(adjustmentId));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_TransferShortfall_Adjustment_AuditOnly_NoMovements()
    {
        // Arrange - opening stock at source warehouse
        var (initScope, _, initInventoryDb, _) = CreateSystemScope();
        try
        {
            using var posterScope = _services.CreateScope();
            var posterHolder = posterScope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
            posterHolder.Scope = SystemBranchScope.Create();
            var poster = posterScope.ServiceProvider.GetRequiredService<StockMovementPoster>();
            var opening = new StockMovement
            {
                MovementId = Guid.NewGuid(),
                MovementType = StockMovement.MovementTypes.OpeningBalance,
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _location1Id,
                Qty = 100m,
                UnitCost = 10.00m,
                SourceDocumentType = "Test",
                SourceDocumentId = Guid.NewGuid(),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            await poster.PostAsync(opening, "inv-032-audit-init", "hash-inv-032-audit-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            // Create and ship an inter-warehouse transfer, then partially receive
            var transferOrchestrator = scope.ServiceProvider.GetRequiredService<TransferOrchestrator>();
            Guid transferId = await transferOrchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await transferOrchestrator.ApproveTransferAsync(transferId);
            await transferOrchestrator.ShipTransferAsync(transferId);
            await transferOrchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 30m);

            // Act - create transfer-shortfall adjustment (audit-only, Posted)
            Guid adjustmentId = await orchestrator.CreateTransferShortfallAdjustmentAsync(
                transferId, "DAMAGED", "Units lost in transit", _branch1Id);

            // Assert - Posted, SourceDocumentType = Transfer
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Posted, adjustment.Status);
            Assert.Equal("Transfer", adjustment.SourceDocumentType);
            Assert.Equal(transferId, adjustment.SourceDocumentId);

            // Assert - no StockMovements created
            var movements = await inventoryDb.StockMovements
                .Where(m => m.SourceDocumentId == adjustmentId && m.SourceDocumentType == "Adjustment")
                .ToListAsync();
            Assert.Empty(movements);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_SubmitForApproval_DraftToPendingApproval()
    {
        // Arrange - Draft adjustment
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 5m) });

            // Assert - Draft
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Draft, adjustment.Status);

            // Act - Submit for approval
            await orchestrator.SubmitForApprovalAsync(adjustmentId);

            // Assert - PendingApproval
            adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.PendingApproval, adjustment.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_Approve_FromPendingApproval()
    {
        // Arrange - Draft adjustment
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 5m) });
            await orchestrator.SubmitForApprovalAsync(adjustmentId);

            // Act - Approve from PendingApproval
            await orchestrator.ApproveAdjustmentAsync(adjustmentId);

            // Assert - Approved
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Approved, adjustment.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_Reject_FromPendingApproval()
    {
        // Arrange - Draft adjustment
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 5m) });
            await orchestrator.SubmitForApprovalAsync(adjustmentId);

            // Act - Reject from PendingApproval
            await orchestrator.RejectAdjustmentAsync(adjustmentId);

            // Assert - Rejected
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Rejected, adjustment.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_040_Adjustment_CannotApprove_FromPosted()
    {
        // Arrange - Posted adjustment
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid adjustmentId = await orchestrator.CreateAdjustmentAsync(
                "FOUND", "Found units", _branch1Id,
                new[] { LineIn(_productId, _warehouse1Id, _location1Id, 5m) });
            await orchestrator.ApproveAdjustmentAsync(adjustmentId);
            await orchestrator.PostAdjustmentAsync(adjustmentId);

            // Act & Assert - cannot approve a Posted adjustment
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ApproveAdjustmentAsync(adjustmentId));
        }
        finally
        {
            scope.Dispose();
        }
    }
}
