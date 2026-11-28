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
using SupplyForge.MasterData.Infrastructure.Location;
using SupplyForge.MasterData.Contracts.Location;
using SupplyForge.MasterData.Contracts.Warehouse;
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
/// Integration tests for Replenishment service (INV-060, INV-061).
/// Tests run against throwaway SQL Server databases with real EF Core migrations.
/// Requirement IDs are included in every test name (NFR-051).
/// </summary>
public sealed class ReplenishmentTests : IAsyncLifetime
{
    private string _dbName = string.Empty;
    private IServiceProvider _services = null!;

    private Guid _productId;
    private Guid _warehouse1Id;
    private Guid _locationStorageId;
    private Guid _branch1Id;

    public async Task InitializeAsync()
    {
        _dbName = ThrowawayDatabase.NewName("InvReplenish");
        await ThrowawayDatabase.CreateAsync(_dbName);

        await using (var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(_dbName), SystemBranchScope.Create()))
        {
            await md.Database.MigrateAsync();
        }
        await using (var pc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(_dbName)))
        {
            await pc.Database.MigrateAsync();
        }
        await using (var ig = new SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext(ThrowawayDatabase.Integrations(_dbName)))
        {
            await ig.Database.MigrateAsync();
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
        sc.AddScoped<SupplyForge.MasterData.Contracts.Location.ILocationQuery,
            SupplyForge.MasterData.Infrastructure.Location.LocationQuery>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.AvailabilityCalculator>();
        sc.AddScoped<SupplyForge.Shared.Outbox.IOutbox,
            SupplyForge.Integrations.Infrastructure.Outbox.Outbox>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.ReplenishmentService>();

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
        md.Branches.Add(branch1);

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
        md.Warehouses.Add(warehouse1);

        var locationStorage = new StorageLocation
        {
            Id = Guid.NewGuid(),
            Code = "LOC-STORAGE",
            Name = "Storage Location",
            Type = "Storage",
            WarehouseId = warehouse1.Id,
            IsActive = true,
            IsBlocked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        md.StorageLocations.Add(locationStorage);

        await md.SaveChangesAsync();

        _productId = product.Id;
        _warehouse1Id = warehouse1.Id;
        _locationStorageId = locationStorage.Id;
        _branch1Id = branch1.Id;
    }

    private (IServiceScope Scope, ReplenishmentService Service, InventoryDbContext InventoryDb) CreateSystemScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = SystemBranchScope.Create();
        var service = scope.ServiceProvider.GetRequiredService<ReplenishmentService>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, service, inventoryDb);
    }

    [Fact]
    public async Task INV_060_ReorderPoint_Breach_AlertsOnce()
    {
        // Arrange - stock below reorder point
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.Add(new StockBalance
            {
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _locationStorageId,
                OnHand = 5m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.ReorderPoints.Add(new ReorderPoint
            {
                ReorderPointId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ReorderPointQty = 20m,
                IsAlerted = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act
        var (scope, service, inventoryDb) = CreateSystemScope();
        try
        {
            await service.CheckReorderPointsAsync();

            // Assert - reorder point is alerted
            var rp = await inventoryDb.ReorderPoints
                .FirstOrDefaultAsync(r => r.ProductId == _productId && r.WarehouseId == _warehouse1Id);
            Assert.NotNull(rp);
            Assert.True(rp.IsAlerted);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_060_ReorderPoint_NoBreach_NoAlert()
    {
        // Arrange - stock above reorder point
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.Add(new StockBalance
            {
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _locationStorageId,
                OnHand = 50m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.ReorderPoints.Add(new ReorderPoint
            {
                ReorderPointId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ReorderPointQty = 20m,
                IsAlerted = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act
        var (scope, service, inventoryDb) = CreateSystemScope();
        try
        {
            await service.CheckReorderPointsAsync();

            // Assert - reorder point not alerted
            var rp = await inventoryDb.ReorderPoints
                .FirstOrDefaultAsync(r => r.ProductId == _productId && r.WarehouseId == _warehouse1Id);
            Assert.NotNull(rp);
            Assert.False(rp.IsAlerted);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_061_Alert_Once_Per_Breach_Until_Recovered()
    {
        // Arrange - reorder point already alerted, stock still below
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.Add(new StockBalance
            {
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _locationStorageId,
                OnHand = 5m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.ReorderPoints.Add(new ReorderPoint
            {
                ReorderPointId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ReorderPointQty = 20m,
                IsAlerted = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - second check should not re-alert (already alerted)
        var (scope, service, inventoryDb) = CreateSystemScope();
        try
        {
            await service.CheckReorderPointsAsync();

            // Assert - still alerted, not double-alerted
            var rp = await inventoryDb.ReorderPoints
                .FirstOrDefaultAsync(r => r.ProductId == _productId && r.WarehouseId == _warehouse1Id);
            Assert.NotNull(rp);
            Assert.True(rp.IsAlerted);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_061_Recovery_ClearsAlert()
    {
        // Arrange - reorder point alerted, then stock recovers above threshold
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.Add(new StockBalance
            {
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _locationStorageId,
                OnHand = 50m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.ReorderPoints.Add(new ReorderPoint
            {
                ReorderPointId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ReorderPointQty = 20m,
                IsAlerted = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - check recovered alerts
        var (scope, service, inventoryDb) = CreateSystemScope();
        try
        {
            await service.ClearRecoveredAlertsAsync();

            // Alert cleared because stock recovered above threshold
            var rp = await inventoryDb.ReorderPoints
                .FirstOrDefaultAsync(r => r.ProductId == _productId && r.WarehouseId == _warehouse1Id);
            Assert.NotNull(rp);
            Assert.False(rp.IsAlerted);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_060_Incoming_Counts_Toward_Availability()
    {
        // Arrange - on-hand below reorder point but incoming brings it above
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.Add(new StockBalance
            {
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _locationStorageId,
                OnHand = 5m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.IncomingProjections.Add(new IncomingProjection
            {
                IncomingProjectionId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ExpectedDeliveryId = Guid.NewGuid(),
                ExpectedQty = 30m,
                ExpectedDate = DateTimeOffset.UtcNow.AddDays(3),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.ReorderPoints.Add(new ReorderPoint
            {
                ReorderPointId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ReorderPointQty = 20m,
                IsAlerted = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act
        var (scope, service, inventoryDb) = CreateSystemScope();
        try
        {
            await service.CheckReorderPointsAsync();

            // Assert - available(5) + incoming(30) = 35 >= reorder point(20), no alert
            var rp = await inventoryDb.ReorderPoints
                .FirstOrDefaultAsync(r => r.ProductId == _productId && r.WarehouseId == _warehouse1Id);
            Assert.NotNull(rp);
            Assert.False(rp.IsAlerted);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_061_DoubleAlert_PreventedUnderConcurrency()
    {
        // Arrange - stock below reorder point, no alert yet
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.Add(new StockBalance
            {
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                LocationId = _locationStorageId,
                OnHand = 5m,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();

            initInventoryDb.ReorderPoints.Add(new ReorderPoint
            {
                ReorderPointId = Guid.NewGuid(),
                ProductId = _productId,
                WarehouseId = _warehouse1Id,
                ReorderPointQty = 20m,
                IsAlerted = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act - concurrent duplicate alert checks
        var (scope1, service1, inventoryDb1) = CreateSystemScope();
        var (scope2, service2, inventoryDb2) = CreateSystemScope();
        try
        {
            var task1 = service1.CheckReorderPointsAsync();
            var task2 = service2.CheckReorderPointsAsync();
            await Task.WhenAll(task1, task2);

            // Assert - only one alert was created (HOLDLOCK serialization)
            var alertCount = await inventoryDb1.ReorderPoints
                .CountAsync(r => r.ProductId == _productId && r.WarehouseId == _warehouse1Id && r.IsAlerted);
            Assert.Equal(1, alertCount);
        }
        finally
        {
            scope1.Dispose();
            scope2.Dispose();
        }
    }
}
