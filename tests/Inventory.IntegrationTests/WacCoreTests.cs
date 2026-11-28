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
using SupplyForge.PlatformCore.Infrastructure.Services;
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
/// Integration tests for WAC Core (A1-6).
/// Tests run against throwaway SQL Server databases with real EF Core migrations.
/// </summary>
public sealed class WacCoreTests : IAsyncLifetime
{
    private string _dbName = string.Empty;
    private IServiceProvider _services = null!;

    // Test data IDs
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
        _dbName = ThrowawayDatabase.NewName("WacTest");
        await ThrowawayDatabase.CreateAsync(_dbName);

        // Apply migrations for all three modules
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

        // Seed minimal master data: company, branches, product, warehouse, location
        await SeedMasterDataAsync();

        // Build DI container with real implementations (matching Host pattern)
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
        sc.AddScoped<ICurrentUser>(_ => new TestCurrentUser());
        sc.AddScoped<ICorrelationIdProvider>(_ => new TestCorrelationIdProvider());
        sc.AddScoped<IBranchGuard>(sp => new BranchGuard(sp.GetRequiredService<ICurrentBranchScope>()));
        sc.AddScoped<SupplyForge.MasterData.Contracts.Location.ILocationQuery,
            SupplyForge.MasterData.Infrastructure.Location.LocationQuery>();

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

        // Create company
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

        // Create three branches
        var branch1 = new Branch { Id = Guid.NewGuid(), Code = "BR1", Name = "Branch 1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var branch2 = new Branch { Id = Guid.NewGuid(), Code = "BR2", Name = "Branch 2", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var branch3 = new Branch { Id = Guid.NewGuid(), Code = "BR3", Name = "Branch 3", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.Branches.AddRange(branch1, branch2, branch3);

        // Create UoM
        var uomId = Guid.NewGuid();
        var uom = new UnitOfMeasure { Id = uomId, Code = "EA", Name = "Each", CreatedAt = DateTimeOffset.UtcNow };
        md.UnitsOfMeasure.Add(uom);

        // Create product
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

        // Create warehouses (one per branch)
        var warehouse1 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-001", Name = "Warehouse 1", BranchId = branch1.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var warehouse2 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-002", Name = "Warehouse 2", BranchId = branch2.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var warehouse3 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-003", Name = "Warehouse 3", BranchId = branch3.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.Warehouses.AddRange(warehouse1, warehouse2, warehouse3);

        // Create locations
        var location1 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-001", Name = "Location 1", Type = "Storage", WarehouseId = warehouse1.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var location2 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-002", Name = "Location 2", Type = "Storage", WarehouseId = warehouse2.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var location3 = new StorageLocation { Id = Guid.NewGuid(), Code = "LOC-003", Name = "Location 3", Type = "Storage", WarehouseId = warehouse3.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        md.StorageLocations.AddRange(location1, location2, location3);

        await md.SaveChangesAsync();

        // Store IDs for tests
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

    private StockMovement CreateMovement(Guid productId, Guid warehouseId, Guid locationId, decimal qty, string movementType, decimal unitCost = 10.00m)
    {
        return new StockMovement
        {
            MovementId = Guid.NewGuid(),
            ProductId = productId,
            WarehouseId = warehouseId,
            LocationId = locationId,
            Qty = qty,
            MovementType = movementType,
            SourceDocumentType = "Test",
            SourceDocumentId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UnitCost = unitCost
        };
    }

    /// <summary>
    /// Creates a new scope with system branch scope set, returns the poster and db context.
    /// </summary>
    private (IServiceScope Scope, StockMovementPoster Poster, InventoryDbContext InventoryDb, BranchScopeHolder Holder) CreateSystemScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = SystemBranchScope.Create();
        var poster = scope.ServiceProvider.GetRequiredService<StockMovementPoster>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, poster, inventoryDb, holder);
    }

    /// <summary>
    /// Creates a new scope with specific branch scope set, returns the poster and db context.
    /// </summary>
    private async Task<(IServiceScope Scope, StockMovementPoster Poster, InventoryDbContext InventoryDb, BranchScopeHolder Holder)> CreateBranchScopeAsync(Guid branchId)
    {
        var scope = _services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IBranchScopeProvider>();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = await provider.ResolveAsync(branchId);
        var poster = scope.ServiceProvider.GetRequiredService<StockMovementPoster>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, poster, inventoryDb, holder);
    }

    /// <summary>
    /// Creates a new scope with company-wide branch scope set, returns the poster and db context.
    /// </summary>
    private (IServiceScope Scope, StockMovementPoster Poster, InventoryDbContext InventoryDb, BranchScopeHolder Holder) CreateCompanyWideScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = new RequestBranchScope(Array.Empty<Guid>(), isCompanyWide: true);
        var poster = scope.ServiceProvider.GetRequiredService<StockMovementPoster>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, poster, inventoryDb, holder);
    }

    [Fact]
    public async Task WAC_02_OpeningBalance_SetsInitialQtyAndCost()
    {
        // Arrange
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var movement = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 15.50m);

            // Act
            await poster.PostAsync(movement, "wac-02-1", "hash-wac-02-1");

            // Assert
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(100, productCost!.QtyOnHand);
            Assert.Equal(15.50m, Math.Round(productCost.AvgCost, 2)); // Scale 8 stored, but compare at 2 for test
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_02_OpeningBalance_MultipleDuringDryRun_Allowed()
    {
        // Arrange
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            // First opening balance
            var movement1 = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 15.50m);
            await poster.PostAsync(movement1, "wac-02-2a", "hash-wac-02-2a");

            // Second opening balance (simulating dry-run reconciliation) - allowed
            var movement2 = CreateMovement(_productId, _warehouse1Id, _location1Id, 50, StockMovement.MovementTypes.OpeningBalance, 16.00m);
            await poster.PostAsync(movement2, "wac-02-2b", "hash-wac-02-2b");

            // Assert - both openings applied (blended)
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(150, productCost!.QtyOnHand);
            // Blended: (100*15.50 + 50*16.00) / 150 = 15.666...
            Assert.Equal(15.66666667m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_03_Receipt_BlendsWAC_Correctly()
    {
        // Arrange - initial stock via opening balance
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-03-init", "hash-wac-03-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - receipt at different cost
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var receipt = CreateMovement(_productId, _warehouse1Id, _location1Id, 50, StockMovement.MovementTypes.Receipt, 12.00m);
            await poster.PostAsync(receipt, "wac-03-1", "hash-wac-03-1");

            // Assert - WAC blended: (100*10 + 50*12) / 150 = 10.666...
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(150, productCost!.QtyOnHand);
            Assert.Equal(10.66666667m, Math.Round(productCost.AvgCost, 8));

            // Verify movement recorded with correct UnitCost
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == receipt.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(12.00m, movement!.UnitCost); // Receipt records its own cost
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_03_Receipt_IntoQuarantine_UpdatesWAC()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-03-q-init", "hash-wac-03-q-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - receipt into quarantine location (different location, same warehouse)
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            // Create quarantine location
            await using var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(_dbName), SystemBranchScope.Create());
            var quarantineLoc = new StorageLocation
            {
                Id = Guid.NewGuid(),
                Code = "QUAR-001",
                Name = "Quarantine",
                Type = "Quarantine",
                WarehouseId = _warehouse1Id,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            md.StorageLocations.Add(quarantineLoc);
            await md.SaveChangesAsync();

            var receipt = CreateMovement(_productId, _warehouse1Id, quarantineLoc.Id, 50, StockMovement.MovementTypes.Receipt, 12.00m);
            await poster.PostAsync(receipt, "wac-03-q-1", "hash-wac-03-q-1");

            // Assert - WAC still updated (quarantine is on-hand in warehouse)
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(150, productCost!.QtyOnHand);
            Assert.Equal(10.66666667m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_04_Issue_RecordsCurrentWAC_DoesNotReaverage()
    {
        // Arrange - initial stock with WAC
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-04-init", "hash-wac-04-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - issue 30 units
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -30, StockMovement.MovementTypes.Issue);
            await poster.PostAsync(issue, "wac-04-1", "hash-wac-04-1");

            // Assert - WAC unchanged at 10.00
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(70, productCost!.QtyOnHand);
            Assert.Equal(10.00m, Math.Round(productCost.AvgCost, 8));

            // Issue movement records current WAC as UnitCost
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == issue.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(10.00m, movement!.UnitCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_04_Issue_ToZero_ResetsWAC()
    {
        // Arrange - initial stock with WAC
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-04-z-init", "hash-wac-04-z-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - issue all 100 units
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -100, StockMovement.MovementTypes.Issue);
            await poster.PostAsync(issue, "wac-04-z-1", "hash-wac-04-z-1");

            // Assert - QtyOnHand = 0, AvgCost = 0
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(0, productCost!.QtyOnHand);
            Assert.Equal(0m, productCost.AvgCost);

            // Issue movement records WAC at time of issue
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == issue.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(10.00m, movement!.UnitCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_05_TransferOut_SourceWAC_Used()
    {
        // Arrange - source warehouse with stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-05-out-init", "hash-wac-05-out-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - TransferOut from source
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var transferOut = CreateMovement(_productId, _warehouse1Id, _location1Id, -30, StockMovement.MovementTypes.TransferOut);
            await poster.PostAsync(transferOut, "wac-05-out-1", "hash-wac-05-out-1");

            // Assert - source WAC used, qty reduced
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(70, productCost!.QtyOnHand);
            Assert.Equal(10.00m, Math.Round(productCost.AvgCost, 8)); // WAC unchanged

            // TransferOut movement records source WAC
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferOut.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(10.00m, movement!.UnitCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_05_TransferIn_DestinationBlendsWithSourceWAC()
    {
        // Arrange - destination warehouse with existing stock at different WAC
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse2Id, _location2Id, 100, StockMovement.MovementTypes.OpeningBalance, 20.00m);
            await initPoster.PostAsync(opening, "wac-05-in-init", "hash-wac-05-in-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - TransferIn at source WAC (10.00) to destination
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var transferIn = CreateMovement(_productId, _warehouse2Id, _location2Id, 30, StockMovement.MovementTypes.TransferIn, 10.00m);
            await poster.PostAsync(transferIn, "wac-05-in-1", "hash-wac-05-in-1");

            // Assert - destination WAC blended: (100*20 + 30*10) / 130 = 17.6923...
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse2Id);
            Assert.NotNull(productCost);
            Assert.Equal(130, productCost!.QtyOnHand);
            Assert.Equal(17.69230769m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_05_SameWarehouseMove_CostUnchanged()
    {
        // Arrange
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-05-same-init", "hash-wac-05-same-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - move within same warehouse (different location)
        await using var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(_dbName), SystemBranchScope.Create());
        var location4 = new StorageLocation
        {
            Id = Guid.NewGuid(),
            Code = "LOC-004",
            Name = "Location 4",
            Type = "Storage",
            WarehouseId = _warehouse1Id,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        md.StorageLocations.Add(location4);
        await md.SaveChangesAsync();

        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            // TransferOut from LOC-001
            var transferOut = CreateMovement(_productId, _warehouse1Id, _location1Id, -30, StockMovement.MovementTypes.TransferOut);
            await poster.PostAsync(transferOut, "wac-05-same-out", "hash-wac-05-same-out");

            // TransferIn to LOC-004 (same warehouse) - using same WAC
            var transferIn = CreateMovement(_productId, _warehouse1Id, location4.Id, 30, StockMovement.MovementTypes.TransferIn, 10.00m);
            await poster.PostAsync(transferIn, "wac-05-same-in", "hash-wac-05-same-in");

            // Assert - WAC unchanged at 10.00, total qty still 100
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(100, productCost!.QtyOnHand);
            Assert.Equal(10.00m, Math.Round(productCost.AvgCost, 8));

            // Both movements recorded
            var movements = await inventoryDb.StockMovements
                .Where(m => m.ProductId == _productId && m.WarehouseId == _warehouse1Id)
                .ToListAsync();
            Assert.Equal(3, movements.Count); // opening + out + in
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_06_SupplierReturn_IssuesAtCurrentWAC()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-06-init", "hash-wac-06-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - Supplier return (Issue type) at current WAC
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var supplierReturn = CreateMovement(_productId, _warehouse1Id, _location1Id, -20, StockMovement.MovementTypes.Issue);
            await poster.PostAsync(supplierReturn, "wac-06-1", "hash-wac-06-1");

            // Assert - WAC unchanged, qty reduced
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(80, productCost!.QtyOnHand);
            Assert.Equal(10.00m, Math.Round(productCost.AvgCost, 8));

            // Supplier return records current WAC
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == supplierReturn.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(10.00m, movement!.UnitCost);
            Assert.Equal(StockMovement.MovementTypes.Issue, movement.MovementType);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_08_ZeroStock_NextReceiptEstablishesNewWAC()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-08-init", "hash-wac-08-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act 1 - Issue all to zero
        var (scope1, poster1, inventoryDb1, _) = CreateSystemScope();
        try
        {
            var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -100, StockMovement.MovementTypes.Issue);
            await poster1.PostAsync(issue, "wac-08-issue", "hash-wac-08-issue");
        }
        finally
        {
            scope1.Dispose();
        }

        // Act 2 - New receipt establishes fresh WAC
        var (scope2, poster2, inventoryDb2, _) = CreateSystemScope();
        try
        {
            var receipt = CreateMovement(_productId, _warehouse1Id, _location1Id, 50, StockMovement.MovementTypes.Receipt, 25.00m);
            await poster2.PostAsync(receipt, "wac-08-receipt", "hash-wac-08-receipt");

            // Assert - New WAC established from receipt alone (25.00)
            var productCost = await inventoryDb2.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(50, productCost!.QtyOnHand);
            Assert.Equal(25.00m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope2.Dispose();
        }
    }

    [Fact]
    public async Task WAC_09_Scale8_IntermediatesRetained()
    {
        // Arrange - opening with cost that produces repeating decimal
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 3, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await poster.PostAsync(opening, "wac-09-init", "hash-wac-09-init");

            // Receipt that creates repeating decimal at scale 8
            var receipt = CreateMovement(_productId, _warehouse1Id, _location1Id, 7, StockMovement.MovementTypes.Receipt, 10.00m / 3.0m); // 3.33333333...
            await poster.PostAsync(receipt, "wac-09-1", "hash-wac-09-1");

            // Assert - AvgCost stored at scale 8
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(10, productCost!.QtyOnHand);
            // (3*10 + 7*3.33333333) / 10 = 53.33333331 / 10 = 5.333333331 -> rounded to 8 decimals
            Assert.Equal(5.33333333m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_10_Rounding_DustOnFinalIssue()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-10-init", "hash-wac-10-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - Issue all 100 units
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -100, StockMovement.MovementTypes.Issue);
            await poster.PostAsync(issue, "wac-10-1", "hash-wac-10-1");

            // Assert - AvgCost reset to 0, no negative WAC
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(0, productCost!.QtyOnHand);
            Assert.Equal(0m, productCost.AvgCost);

            // Issue movement records the WAC at time of issue (residual dust assigned to this issue)
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == issue.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(10.00m, movement!.UnitCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_080_IssueMovement_RecordsCostUsed()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 15.75m);
            await initPoster.PostAsync(opening, "inv-080-init", "hash-inv-080-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - Issue movement
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -25, StockMovement.MovementTypes.Issue);
            await poster.PostAsync(issue, "inv-080-1", "hash-inv-080-1");

            // Assert - StockMovement.UnitCost = WAC at time of issue
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == issue.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(15.75m, movement!.UnitCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_081_StockValue_Calculable()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 12.50m);
            await initPoster.PostAsync(opening, "inv-081-init", "hash-inv-081-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - Receipt blends
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var receipt = CreateMovement(_productId, _warehouse1Id, _location1Id, 50, StockMovement.MovementTypes.Receipt, 15.00m);
            await poster.PostAsync(receipt, "inv-081-1", "hash-inv-081-1");

            // Assert - Stock value = QtyOnHand * AvgCost
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(150, productCost!.QtyOnHand);
            // (100*12.50 + 50*15.00) / 150 = 2000 / 150 = 13.33333333
            decimal expectedValue = 150 * 13.33333333m;
            Assert.Equal(Math.Round(expectedValue, 2), Math.Round(productCost.QtyOnHand * productCost.AvgCost, 2));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_ConcurrentReceiptAndIssue_NoLostUpdate()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-conc-init", "hash-wac-conc-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - concurrent receipt and issue
        var tasks = new List<Task>();
        int receiptCount = 5;
        int issueCount = 5;

        for (int i = 0; i < receiptCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var (scope, poster, _, _) = CreateSystemScope();
                try
                {
                    var receipt = CreateMovement(_productId, _warehouse1Id, _location1Id, 10, StockMovement.MovementTypes.Receipt, 12.00m);
                    await poster.PostAsync(receipt, $"wac-conc-receipt-{Guid.NewGuid()}", $"hash-conc-receipt-{Guid.NewGuid()}");
                }
                finally
                {
                    scope.Dispose();
                }
            }));
        }

        for (int i = 0; i < issueCount; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var (scope, poster, _, _) = CreateSystemScope();
                try
                {
                    var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -5, StockMovement.MovementTypes.Issue);
                    await poster.PostAsync(issue, $"wac-conc-issue-{Guid.NewGuid()}", $"hash-conc-issue-{Guid.NewGuid()}");
                }
                finally
                {
                    scope.Dispose();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - final state consistent
        var (assertScope, _, assertInventoryDb, _) = CreateSystemScope();
        try
        {
            var productCost = await assertInventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            // Initial 100 + 5*10 (receipts) - 5*5 (issues) = 125
            Assert.Equal(125, productCost!.QtyOnHand);

            var movements = await assertInventoryDb.StockMovements.Where(m => m.ProductId == _productId).ToListAsync();
            Assert.Equal(1 + receiptCount + issueCount, movements.Count); // 1 opening + 5 receipts + 5 issues
        }
        finally
        {
            assertScope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_CrossBranch_Isolation()
    {
        // Arrange - set scope to branch 1
        var (scope, poster, inventoryDb, _) = await CreateBranchScopeAsync(_branch1Id);
        try
        {
            // Act & Assert - try to post to warehouse in branch 2
            var movement = CreateMovement(_productId, _warehouse2Id, _location2Id, 10, StockMovement.MovementTypes.Receipt, 10.00m);
            await Assert.ThrowsAsync<BranchNotFoundException>(async () =>
                await poster.PostAsync(movement, "wac-cross-1", "hash-wac-cross-1"));

            // Verify no movement recorded
            var movements = await inventoryDb.StockMovements.Where(m => m.ProductId == _productId).ToListAsync();
            Assert.Empty(movements);

            // Verify no ProductCost created
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse2Id);
            Assert.Null(productCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_02_OpeningBalance_RejectsNegativeQuantity()
    {
        // Arrange
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var movement = CreateMovement(_productId, _warehouse1Id, _location1Id, -10, StockMovement.MovementTypes.OpeningBalance, 15.50m);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await poster.PostAsync(movement, "wac-02-neg", "hash-wac-02-neg"));

            // Verify no ProductCost created
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.Null(productCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_05_TransferOut_RejectsWhenExceedsAvailableStock()
    {
        // Arrange - source warehouse with limited stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 10, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-05-ex-init", "hash-wac-05-ex-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act & Assert - try to transfer out more than available
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var transferOut = CreateMovement(_productId, _warehouse1Id, _location1Id, -20, StockMovement.MovementTypes.TransferOut);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await poster.PostAsync(transferOut, "wac-05-ex-1", "hash-wac-05-ex-1"));

            // Verify stock unchanged
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(10m, productCost!.QtyOnHand);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_05_TransferIn_RejectsNegativeSourceCost()
    {
        // Arrange
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var transferIn = CreateMovement(_productId, _warehouse1Id, _location1Id, 10, StockMovement.MovementTypes.TransferIn, -5.00m);
            await Assert.ThrowsAsync<ArgumentException>(async () =>
                await poster.PostAsync(transferIn, "wac-05-negcost", "hash-wac-05-negcost"));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_04_Issue_RejectsWhenZeroStock()
    {
        // Arrange - zero stock bucket
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 10, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-04-zs-init", "hash-wac-04-zs-init");

            var issueAll = CreateMovement(_productId, _warehouse1Id, _location1Id, -10, StockMovement.MovementTypes.Issue);
            await initPoster.PostAsync(issueAll, "wac-04-zs-issueall", "hash-wac-04-zs-issueall");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act & Assert - try to issue from zero stock
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var issue = CreateMovement(_productId, _warehouse1Id, _location1Id, -1, StockMovement.MovementTypes.Issue);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await poster.PostAsync(issue, "wac-04-zs-1", "hash-wac-04-zs-1"));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_03_AdjustmentIn_BlendsWAC_LikeReceipt()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-03-adj-init", "hash-wac-03-adj-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - AdjustmentIn at different cost
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var adjIn = CreateMovement(_productId, _warehouse1Id, _location1Id, 50, StockMovement.MovementTypes.AdjustmentIn, 14.00m);
            await poster.PostAsync(adjIn, "wac-03-adj-1", "hash-wac-03-adj-1");

            // Assert - WAC blended like a receipt: (100*10 + 50*14) / 150 = 11.33333333
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(150m, productCost!.QtyOnHand);
            Assert.Equal(11.33333333m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_04_AdjustmentOut_RecordsCurrentWAC()
    {
        // Arrange - initial stock
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-04-adjout-init", "hash-wac-04-adjout-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - AdjustmentOut at current WAC
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var adjOut = CreateMovement(_productId, _warehouse1Id, _location1Id, -25, StockMovement.MovementTypes.AdjustmentOut);
            await poster.PostAsync(adjOut, "wac-04-adjout-1", "hash-wac-04-adjout-1");

            // Assert - WAC unchanged, UnitCost records current WAC
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(75m, productCost!.QtyOnHand);
            Assert.Equal(10.00m, Math.Round(productCost.AvgCost, 8));

            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == adjOut.SourceDocumentId);
            Assert.NotNull(movement);
            Assert.Equal(10.00m, movement!.UnitCost);
            Assert.Equal(StockMovement.MovementTypes.AdjustmentOut, movement.MovementType);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_Concurrent_DifferentLocations_SameWarehouse_NoLostUpdate()
    {
        // Arrange - initial stock across two locations in same warehouse
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.00m);
            await initPoster.PostAsync(opening, "wac-conc-loc-init", "hash-wac-conc-loc-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - concurrent movements at different locations in same warehouse
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var (scope, poster, _, _) = CreateSystemScope();
                try
                {
                    var movement = CreateMovement(_productId, _warehouse1Id, _location1Id, 10, StockMovement.MovementTypes.Receipt, 12.00m);
                    await poster.PostAsync(movement, $"wac-conc-loc1-{Guid.NewGuid()}", $"hash-conc-loc1-{Guid.NewGuid()}");
                }
                finally
                {
                    scope.Dispose();
                }
            }));
            tasks.Add(Task.Run(async () =>
            {
                var (scope, poster, _, _) = CreateSystemScope();
                try
                {
                    var movement = CreateMovement(_productId, _warehouse1Id, _location2Id, 5, StockMovement.MovementTypes.Receipt, 14.00m);
                    await poster.PostAsync(movement, $"wac-conc-loc2-{Guid.NewGuid()}", $"hash-conc-loc2-{Guid.NewGuid()}");
                }
                finally
                {
                    scope.Dispose();
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert - final state consistent (ProductCost shared across locations)
        var (assertScope, _, assertInventoryDb, _) = CreateSystemScope();
        try
        {
            var productCost = await assertInventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            // Initial 100 + 5*10 (loc1) + 5*5 (loc2) = 175
            Assert.Equal(175m, productCost!.QtyOnHand);

            var movements = await assertInventoryDb.StockMovements.Where(m => m.ProductId == _productId).ToListAsync();
            Assert.Equal(11, movements.Count); // 1 opening + 10 concurrent
        }
        finally
        {
            assertScope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_Scale8_FractionalCost_PreservedThroughTransferOut_TransferIn()
    {
        // Arrange - source warehouse with stock at fractional WAC (scale 8)
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 10.12345678m);
            await initPoster.PostAsync(opening, "wac-scale8-init", "hash-wac-scale8-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - TransferOut from source, then TransferIn to destination
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var transferOut = CreateMovement(_productId, _warehouse1Id, _location1Id, -30, StockMovement.MovementTypes.TransferOut);
            await poster.PostAsync(transferOut, "wac-scale8-out", "hash-wac-scale8-out");

            // Verify TransferOut.UnitCost preserves scale-8 source WAC
            var outMovement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferOut.SourceDocumentId);
            Assert.NotNull(outMovement);
            Assert.Equal(10.12345678m, outMovement!.UnitCost);

            // TransferIn to destination warehouse at source WAC
            var transferIn = CreateMovement(_productId, _warehouse2Id, _location2Id, 30, StockMovement.MovementTypes.TransferIn, 10.12345678m);
            await poster.PostAsync(transferIn, "wac-scale8-in", "hash-wac-scale8-in");

            // Verify TransferIn.UnitCost preserves scale-8 source WAC
            var inMovement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == transferIn.SourceDocumentId);
            Assert.NotNull(inMovement);
            Assert.Equal(10.12345678m, inMovement!.UnitCost);

            // Verify destination WAC matches source WAC (30 units at 10.12345678)
            var destCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse2Id);
            Assert.NotNull(destCost);
            Assert.Equal(30m, destCost.QtyOnHand);
            Assert.Equal(10.12345678m, Math.Round(destCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_Scale8_MultiStepTransfer_DestinationWACMatchesSource()
    {
        // Arrange - source warehouse with stock at fractional WAC
        var (initScope, initPoster, initInventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 7.77777777m);
            await initPoster.PostAsync(opening, "wac-multistep-init", "hash-wac-multistep-init");
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - TransferOut 40 units, TransferIn 40 units to destination
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var transferOut = CreateMovement(_productId, _warehouse1Id, _location1Id, -40, StockMovement.MovementTypes.TransferOut);
            await poster.PostAsync(transferOut, "wac-multistep-out", "hash-wac-multistep-out");

            var transferIn = CreateMovement(_productId, _warehouse2Id, _location2Id, 40, StockMovement.MovementTypes.TransferIn, 7.77777777m);
            await poster.PostAsync(transferIn, "wac-multistep-in", "hash-wac-multistep-in");

            // Assert - source remaining stock keeps WAC
            var srcCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(srcCost);
            Assert.Equal(60m, srcCost.QtyOnHand);
            Assert.Equal(7.77777777m, Math.Round(srcCost.AvgCost, 8));

            // Assert - destination WAC matches source WAC exactly
            var destCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse2Id);
            Assert.NotNull(destCost);
            Assert.Equal(40m, destCost.QtyOnHand);
            Assert.Equal(7.77777777m, Math.Round(destCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task WAC_Scale8_OpeningBalance_FractionalCostPreserved()
    {
        // Arrange & Act - opening balance with fractional unit cost
        var (scope, poster, inventoryDb, _) = CreateSystemScope();
        try
        {
            var opening = CreateMovement(_productId, _warehouse1Id, _location1Id, 100, StockMovement.MovementTypes.OpeningBalance, 3.33333333m);
            await poster.PostAsync(opening, "wac-scale8-open", "hash-wac-scale8-open");

            // Assert - WAC preserves scale-8 fractional cost
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(100m, productCost.QtyOnHand);
            Assert.Equal(3.33333333m, Math.Round(productCost.AvgCost, 8));

            // Verify movement UnitCost preserves scale-8
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.ProductId == _productId && m.WarehouseId == _warehouse1Id);
            Assert.NotNull(movement);
            Assert.Equal(3.33333333m, movement!.UnitCost);
        }
        finally
        {
            scope.Dispose();
        }
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
    }

    private sealed class TestCorrelationIdProvider : ICorrelationIdProvider
    {
        public string? GetCorrelationId() => null;
    }
}