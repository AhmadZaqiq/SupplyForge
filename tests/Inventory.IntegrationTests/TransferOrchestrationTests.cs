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
/// Integration tests for Transfer orchestration (A1-7).
/// Tests run against throwaway SQL Server databases with real EF Core migrations.
/// Requirement IDs are included in every test name (NFR-051).
/// </summary>
public sealed class TransferOrchestrationTests : IAsyncLifetime
{
    private string _dbName = string.Empty;
    private IServiceProvider _services = null!;

    private Guid _productId;
    private Guid _warehouse1Id;
    private Guid _warehouse2Id;
    private Guid _warehouse3Id;
    private Guid _location1Id;
    private Guid _location2Id;
    private Guid _location3Id;
    private Guid _branch1Id;
    private Guid _branch2Id;
    private Guid _branch3Id;

    public async Task InitializeAsync()
    {
        _dbName = ThrowawayDatabase.NewName("InvTransfer");
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
        var branch3 = new Branch { Id = Guid.NewGuid(), Code = "BR3", Name = "Branch 3", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.Branches.AddRange(branch1, branch2, branch3);

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
        var warehouse3 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-003", Name = "Warehouse 3", BranchId = branch3.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.Warehouses.AddRange(warehouse1, warehouse2, warehouse3);

        var location1 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-001", Name = "Location 1", Type = "Storage", WarehouseId = warehouse1.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var location2 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-002", Name = "Location 2", Type = "Storage", WarehouseId = warehouse2.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var location3 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-003", Name = "Location 3", Type = "Storage", WarehouseId = warehouse3.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.StorageLocations.AddRange(location1, location2, location3);

        await md.SaveChangesAsync();

        _productId = product.Id;
        _warehouse1Id = warehouse1.Id;
        _warehouse2Id = warehouse2.Id;
        _warehouse3Id = warehouse3.Id;
        _location1Id = location1.Id;
        _location2Id = location2.Id;
        _location3Id = location3.Id;
        _branch1Id = branch1.Id;
        _branch2Id = branch2.Id;
        _branch3Id = branch3.Id;
    }

    private (IServiceScope Scope, TransferOrchestrator Orchestrator, InventoryDbContext InventoryDb, BranchScopeHolder Holder) CreateSystemScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = SystemBranchScope.Create();
        var orchestrator = scope.ServiceProvider.GetRequiredService<TransferOrchestrator>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, orchestrator, inventoryDb, holder);
    }

    private async Task<(IServiceScope Scope, TransferOrchestrator Orchestrator, InventoryDbContext InventoryDb, BranchScopeHolder Holder)> CreateBranchScopeAsync(Guid branchId)
    {
        var scope = _services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IBranchScopeProvider>();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = await provider.ResolveAsync(branchId);
        var orchestrator = scope.ServiceProvider.GetRequiredService<TransferOrchestrator>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, orchestrator, inventoryDb, holder);
    }

    [Fact]
    public async Task INV_030_Transfer_SameWarehouse_LocationMove_Immediate()
    {
        // Arrange - opening stock at source location
        var (initScope, _, initInventoryDb, _) = CreateSystemScope();
        try
        {
            using var posterScope = _services.CreateScope();
            var posterHolder = posterScope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
            posterHolder.Scope = SystemBranchScope.Create();
            var poster = posterScope.ServiceProvider.GetRequiredService<StockMovementPoster>();
            var movement = new StockMovement
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
            await poster.PostAsync(movement, "inv-030-same-init", "hash-inv-030-same-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - create, approve, ship (same-warehouse: both legs atomically → Closed)
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse1Id, _location2Id,
                _productId, 30m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            // Assert - transfer is Closed, both legs posted
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Closed, transfer.Status);
            Assert.Equal(30m, transfer.ShippedQty);
            Assert.Equal(30m, transfer.ReceivedQty);
            Assert.Equal(0m, transfer.RemainingQty);

            // Assert - TransferOut posted at source location
            var transferOut = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferId && m.MovementType == StockMovement.MovementTypes.TransferOut);
            Assert.NotNull(transferOut);
            Assert.Equal(-30m, transferOut.Qty);
            Assert.Equal(_warehouse1Id, transferOut.WarehouseId);
            Assert.Equal(_location1Id, transferOut.LocationId);

            // Assert - TransferIn posted at dest location (same warehouse)
            var transferIn = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferId && m.MovementType == StockMovement.MovementTypes.TransferIn);
            Assert.NotNull(transferIn);
            Assert.Equal(30m, transferIn.Qty);
            Assert.Equal(_warehouse1Id, transferIn.WarehouseId);
            Assert.Equal(_location2Id, transferIn.LocationId);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_InterWarehouse_SourceWAC_Preserved()
    {
        // Arrange - source warehouse has stock at WAC 10.00
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
            await poster.PostAsync(opening, "inv-030-inter-init", "hash-inv-030-inter-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - create, approve, ship (inter-warehouse: TransferOut → In Transit)
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 30m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            // Assert - transfer is In Transit
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.InTransit, transfer.Status);
            Assert.Equal(30m, transfer.ShippedQty);
            Assert.Equal(0m, transfer.ReceivedQty);

            // Assert - TransferOut recorded source WAC
            var transferOut = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferId && m.MovementType == StockMovement.MovementTypes.TransferOut);
            Assert.NotNull(transferOut);
            Assert.Equal(10.00m, transferOut.UnitCost); // Source WAC

            // Act - receive at destination (uses source WAC)
            Guid receiveId = Guid.NewGuid();
            await orchestrator.ReceiveTransferAsync(transferId, receiveId, 30m);

            // Assert - TransferIn at source WAC (10.00), destination WAC blends
            transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Closed, transfer.Status);
            Assert.Equal(30m, transfer.ReceivedQty);

            var transferIn = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == receiveId && m.MovementType == StockMovement.MovementTypes.TransferIn);
            Assert.NotNull(transferIn);
            Assert.Equal(30m, transferIn.Qty);
            Assert.Equal(10.00m, transferIn.UnitCost); // Source WAC preserved

            // Destination warehouse (was empty) now has WAC = 10.00
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse2Id);
            Assert.NotNull(productCost);
            Assert.Equal(30m, productCost.QtyOnHand);
            Assert.Equal(10.00m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_MultiplePartialReceipts()
    {
        // Arrange - source with stock, inter-warehouse transfer
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
            await poster.PostAsync(opening, "inv-030-partial-init", "hash-inv-030-partial-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 100m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            // Receive 30
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 30m);
            // Receive 20
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 20m);
            // Receive 50
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 50m);

            // Assert - fully received, Closed
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Closed, transfer.Status);
            Assert.Equal(100m, transfer.ShippedQty);
            Assert.Equal(100m, transfer.ReceivedQty);
            Assert.Equal(0m, transfer.RemainingQty);

            // Assert - source WAC from TransferOut
            var transferOut = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferId && m.MovementType == StockMovement.MovementTypes.TransferOut);
            Assert.NotNull(transferOut);
            decimal sourceWac = transferOut.UnitCost;

            // Assert - three TransferIn movements, each at source WAC
            var transferIns = await inventoryDb.StockMovements
                .Where(m => m.SourceDocumentId != transferId && m.MovementType == StockMovement.MovementTypes.TransferIn && m.WarehouseId == _warehouse2Id)
                .ToListAsync();
            Assert.Equal(3, transferIns.Count);
            foreach (var transferIn in transferIns)
            {
                Assert.Equal(sourceWac, transferIn.UnitCost);
            }
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_DuplicateReceipt_NoDoublePost()
    {
        // Arrange - inter-warehouse transfer, shipped
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
            await poster.PostAsync(opening, "inv-030-dupe-init", "hash-inv-030-dupe-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            Guid receiveId = Guid.NewGuid();

            // First receive
            await orchestrator.ReceiveTransferAsync(transferId, receiveId, 50m);

            // Duplicate receive with same receiveId (replay)
            await orchestrator.ReceiveTransferAsync(transferId, receiveId, 50m);

            // Assert - ReceivedQty not doubled
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(50m, transfer.ReceivedQty);
            Assert.Equal(Transfer.Statuses.Closed, transfer.Status);

            // Assert - only one TransferIn movement
            var transferIns = await inventoryDb.StockMovements
                .Where(m => m.SourceDocumentId == receiveId && m.MovementType == StockMovement.MovementTypes.TransferIn)
                .ToListAsync();
            Assert.Single(transferIns);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_ReceiveExceedsShipped_Rejected()
    {
        // Arrange - inter-warehouse transfer, shipped 50
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
            await poster.PostAsync(opening, "inv-030-exceed-init", "hash-inv-030-exceed-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            // Act & Assert - try to receive 60 (exceeds shipped 50)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 60m));

            // Assert - nothing received
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(0m, transfer.ReceivedQty);
            Assert.Equal(Transfer.Statuses.InTransit, transfer.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_031_Transfer_InTransit_StockNotAvailable()
    {
        // Arrange - inter-warehouse transfer shipped (In Transit)
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
            await poster.PostAsync(opening, "inv-031-init", "hash-inv-031-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 30m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            // Assert - source stock deducted (TransferOut posted)
            var productCostSource = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCostSource);
            Assert.Equal(70m, productCostSource.QtyOnHand); // 100 - 30

            // Assert - transfer is In Transit, dest has no stock yet
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.InTransit, transfer.Status);

            var productCostDest = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse2Id);
            Assert.Null(productCostDest); // nothing received at dest yet
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_Shortfall_CreatesAdjustment()
    {
        // Arrange - inter-warehouse transfer, partially received
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
            await poster.PostAsync(opening, "inv-032-init", "hash-inv-032-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 100m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            // Receive only 70 of 100 (shortfall of 30)
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 70m);

            // Assert - Close rejected because shortfall not reconciled
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CloseTransferAsync(transferId));

            // Assert - create transfer-shortfall adjustment (audit-only, no movements)
            var adjOrchestrator = scope.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
            Guid adjustmentId = await adjOrchestrator.CreateTransferShortfallAdjustmentAsync(
                transferId, "DAMAGED", "30 units lost in transit", _branch1Id);

            // Assert - adjustment is Posted, SourceDocumentType = Transfer
            Adjustment? adjustment = await inventoryDb.Adjustments.FindAsync([adjustmentId]);
            Assert.NotNull(adjustment);
            Assert.Equal(Adjustment.Statuses.Posted, adjustment.Status);
            Assert.Equal("Transfer", adjustment.SourceDocumentType);
            Assert.Equal(transferId, adjustment.SourceDocumentId);

            // Assert - no StockMovements created for the shortfall adjustment
            var shortfallMovements = await inventoryDb.StockMovements
                .Where(m => m.SourceDocumentId == adjustmentId && m.SourceDocumentType == "Adjustment")
                .ToListAsync();
            Assert.Empty(shortfallMovements);

            // Now close should succeed
            await orchestrator.CloseTransferAsync(transferId);
            Transfer? closed = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(closed);
            Assert.Equal(Transfer.Statuses.Closed, closed.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_DuplicateAdjustment_Prevented()
    {
        // Arrange - inter-warehouse transfer, partially received
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
            await poster.PostAsync(opening, "inv-032-dupe-init", "hash-inv-032-dupe-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 100m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 70m);

            var adjOrchestrator = scope.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();

            // First shortfall adjustment
            await adjOrchestrator.CreateTransferShortfallAdjustmentAsync(
                transferId, "DAMAGED", "30 units lost", _branch1Id);

            // Act & Assert - duplicate shortfall adjustment rejected
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adjOrchestrator.CreateTransferShortfallAdjustmentAsync(
                    transferId, "DAMAGED", "duplicate", _branch1Id));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_CloseWithoutAdjustment_Rejected()
    {
        // Arrange - inter-warehouse transfer, partially received, no adjustment
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
            await poster.PostAsync(opening, "inv-032-close-init", "hash-inv-032-close-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 100m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 70m);

            // Act & Assert - close rejected without shortfall adjustment
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CloseTransferAsync(transferId));

            // Assert - transfer still Received (not Closed)
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Received, transfer.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task BRN_012_Transfer_CrossBranch_Rejected()
    {
        // Arrange - branch 1 scope
        var (scope, orchestrator, _, _) = await CreateBranchScopeAsync(_branch1Id);
        try
        {
            // Act & Assert - warehouse1 (branch1) to warehouse3 (branch3) = cross-branch
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CreateTransferAsync(
                    _warehouse1Id, _location1Id,
                    _warehouse3Id, _location3Id,
                    _productId, 10m));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task BRN_021_Transfer_InterBranch_Rejected_UntilB1()
    {
        // Arrange - system scope can access all branches, but inter-branch still rejected
        var (scope, orchestrator, _, _) = CreateSystemScope();
        try
        {
            // Act & Assert - source branch1, dest branch3 = inter-branch, rejected even with system scope
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CreateTransferAsync(
                    _warehouse1Id, _location1Id,
                    _warehouse3Id, _location3Id,
                    _productId, 10m));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task IDEM_Transfer_DuplicateKey_NoDoublePost()
    {
        // Arrange - inter-warehouse transfer, shipped
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
            await poster.PostAsync(opening, "idem-transfer-init", "hash-idem-transfer-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);

            Guid receiveId = Guid.NewGuid();

            // First receive with receiveId
            await orchestrator.ReceiveTransferAsync(transferId, receiveId, 50m);

            // Retry with same receiveId (same idempotency key) - should be a replay
            await orchestrator.ReceiveTransferAsync(transferId, receiveId, 50m);

            // Assert - ReceivedQty = 50, not 100
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(50m, transfer.ReceivedQty);

            // Assert - only one TransferIn movement
            var transferIns = await inventoryDb.StockMovements
                .Where(m => m.SourceDocumentId == receiveId && m.MovementType == StockMovement.MovementTypes.TransferIn)
                .ToListAsync();
            Assert.Single(transferIns);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_CloseCancelled_Rejected()
    {
        // Arrange - inter-warehouse transfer, shipped
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
            await poster.PostAsync(opening, "inv-030-close-cancel-init", "hash-inv-030-close-cancel-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.CancelTransferAsync(transferId);

            // Act & Assert - cannot close a Cancelled transfer
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CloseTransferAsync(transferId));

            // Assert - transfer remains Cancelled
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Cancelled, transfer.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_CloseClosed_Rejected()
    {
        // Arrange - inter-warehouse transfer, shipped and fully received
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
            await poster.PostAsync(opening, "inv-030-close-closed-init", "hash-inv-030-close-closed-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 50m);

            // Transfer is now Closed
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Closed, transfer.Status);

            // Act & Assert - cannot close an already-Closed transfer
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CloseTransferAsync(transferId));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_030_Transfer_CloseDraft_Rejected()
    {
        // Arrange - Draft transfer only
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 10m);

            // Act & Assert - cannot close a Draft transfer
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.CloseTransferAsync(transferId));

            // Assert - transfer remains Draft
            Transfer? transfer = await inventoryDb.Transfers.FindAsync([transferId]);
            Assert.NotNull(transfer);
            Assert.Equal(Transfer.Statuses.Draft, transfer.Status);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_Shortfall_NonExistentTransfer_Rejected()
    {
        // Arrange
        var (scope, _, _, _) = CreateSystemScope();
        try
        {
            var adjOrchestrator = scope.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
            Guid nonExistentTransferId = Guid.NewGuid();

            // Act & Assert - shortfall adjustment for non-existent transfer
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adjOrchestrator.CreateTransferShortfallAdjustmentAsync(
                    nonExistentTransferId, "DAMAGED", "No such transfer", _branch1Id));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_Shortfall_FullyReceived_Rejected()
    {
        // Arrange - inter-warehouse transfer, fully received
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
            await poster.PostAsync(opening, "inv-032-full-init", "hash-inv-032-full-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            var adjOrchestrator = scope.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 50m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 50m);

            // Act & Assert - no shortfall exists (fully received)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adjOrchestrator.CreateTransferShortfallAdjustmentAsync(
                    transferId, "DAMAGED", "No shortfall", _branch1Id));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_Shortfall_WrongStatus_Rejected()
    {
        // Arrange - Draft transfer (not InTransit/Received)
        var (scope, _, _, _) = CreateSystemScope();
        try
        {
            var adjOrchestrator = scope.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
            Guid transferId = await scope.ServiceProvider.GetRequiredService<TransferOrchestrator>().CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 10m);

            // Act & Assert - shortfall adjustment requires InTransit/Received
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                adjOrchestrator.CreateTransferShortfallAdjustmentAsync(
                    transferId, "DAMAGED", "Wrong status", _branch1Id));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_032_Transfer_Shortfall_Concurrent_DuplicatePrevented()
    {
        // Arrange - inter-warehouse transfer, partially received
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
            await poster.PostAsync(opening, "inv-032-concurrent-init", "hash-inv-032-concurrent-init");
        }
        finally
        {
            initScope.Dispose();
        }

        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid transferId = await orchestrator.CreateTransferAsync(
                _warehouse1Id, _location1Id,
                _warehouse2Id, _location2Id,
                _productId, 100m);
            await orchestrator.ApproveTransferAsync(transferId);
            await orchestrator.ShipTransferAsync(transferId);
            await orchestrator.ReceiveTransferAsync(transferId, Guid.NewGuid(), 70m);

            // Act - concurrent duplicate shortfall adjustment creation (separate scopes per request)
            var (scope1, _, _, _) = CreateSystemScope();
            var (scope2, _, _, _) = CreateSystemScope();
            try
            {
                var adjOrchestrator1 = scope1.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
                var adjOrchestrator2 = scope2.ServiceProvider.GetRequiredService<AdjustmentOrchestrator>();
                var task1 = Task.Run<Guid?>(async () => {
                    try { return await adjOrchestrator1.CreateTransferShortfallAdjustmentAsync(transferId, "DAMAGED", "First", _branch1Id); }
                    catch { return null; }
                });
                var task2 = Task.Run<Guid?>(async () => {
                    try { return await adjOrchestrator2.CreateTransferShortfallAdjustmentAsync(transferId, "DAMAGED", "Second", _branch1Id); }
                    catch { return null; }
                });

                var results = await Task.WhenAll(task1, task2);

                // Assert - exactly one succeeds, the other faults due to duplicate prevention
                Assert.Equal(1, results.Count(id => id != null));
                Assert.Equal(1, results.Count(id => id == null));

                // Assert - only one shortfall adjustment exists
                var adjustments = await inventoryDb.Adjustments
                    .Where(a => a.SourceDocumentType == "Transfer" && a.SourceDocumentId == transferId)
                    .ToListAsync();
                Assert.Single(adjustments);
            }
            finally
            {
                scope1.Dispose();
                scope2.Dispose();
            }
        }
        finally
        {
            scope.Dispose();
        }
    }
}
