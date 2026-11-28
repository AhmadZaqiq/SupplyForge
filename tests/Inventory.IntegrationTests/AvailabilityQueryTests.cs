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
/// Integration tests for Availability query (INV-100).
/// Tests run against throwaway SQL Server databases with real EF Core migrations.
/// Requirement IDs are included in every test name (NFR-051).
/// </summary>
public sealed class AvailabilityQueryTests : IAsyncLifetime
{
    private string _dbName = string.Empty;
    private IServiceProvider _services = null!;

    private Guid _productId;
    private Guid _warehouse1Id;
    private Guid _locationStorageId;
    private Guid _locationReturnsId;
    private Guid _locationQuarantineId;
    private Guid _branch1Id;

    public async Task InitializeAsync()
    {
        _dbName = ThrowawayDatabase.NewName("InvAvail");
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
        sc.AddScoped<SupplyForge.MasterData.Contracts.Location.ILocationQuery,
            SupplyForge.MasterData.Infrastructure.Location.LocationQuery>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.AvailabilityCalculator>();
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.AvailabilityQueryService>();
        sc.AddScoped<IBranchGuard>(sp => new BranchGuard(sp.GetRequiredService<ICurrentBranchScope>()));

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
        var locationReturns = new StorageLocation
        {
            Id = Guid.NewGuid(),
            Code = "LOC-RETURNS",
            Name = "Returns Location",
            Type = "Returns",
            WarehouseId = warehouse1.Id,
            IsActive = true,
            IsBlocked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var locationQuarantine = new StorageLocation
        {
            Id = Guid.NewGuid(),
            Code = "LOC-QUARANTINE",
            Name = "Quarantine Location",
            Type = "Quarantine",
            WarehouseId = warehouse1.Id,
            IsActive = true,
            IsBlocked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };
        md.StorageLocations.AddRange(locationStorage, locationReturns, locationQuarantine);

        await md.SaveChangesAsync();

        _productId = product.Id;
        _warehouse1Id = warehouse1.Id;
        _locationStorageId = locationStorage.Id;
        _locationReturnsId = locationReturns.Id;
        _locationQuarantineId = locationQuarantine.Id;
        _branch1Id = branch1.Id;
    }

    private (IServiceScope Scope, AvailabilityQueryService Service, InventoryDbContext InventoryDb) CreateSystemScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = SystemBranchScope.Create();
        var service = scope.ServiceProvider.GetRequiredService<AvailabilityQueryService>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, service, inventoryDb);
    }

    [Fact]
    public async Task INV_100_Availability_AllBuckets()
    {
        // Arrange - stock at Storage (eligible), Returns, Quarantine locations
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.AddRange(
                new StockBalance
                {
                    ProductId = _productId,
                    WarehouseId = _warehouse1Id,
                    LocationId = _locationStorageId,
                    OnHand = 100m,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new StockBalance
                {
                    ProductId = _productId,
                    WarehouseId = _warehouse1Id,
                    LocationId = _locationReturnsId,
                    OnHand = 10m,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new StockBalance
                {
                    ProductId = _productId,
                    WarehouseId = _warehouse1Id,
                    LocationId = _locationQuarantineId,
                    OnHand = 5m,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            await initInventoryDb.SaveChangesAsync();

            // Incoming projection
            initInventoryDb.IncomingProjections.Add(
                new IncomingProjection
                {
                    IncomingProjectionId = Guid.NewGuid(),
                    ProductId = _productId,
                    WarehouseId = _warehouse1Id,
                    ExpectedDeliveryId = Guid.NewGuid(),
                    ExpectedQty = 20m,
                    ExpectedDate = DateTimeOffset.UtcNow.AddDays(5),
                    CreatedAt = DateTimeOffset.UtcNow
                });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act
        var (scope, service, _) = CreateSystemScope();
        try
        {
            var result = await service.GetAvailabilityAsync(_productId, _warehouse1Id);

            // Assert - all buckets
            Assert.Equal(115m, result.OnHand); // 100 + 10 + 5
            Assert.Equal(100m, result.Available); // Storage only (eligible)
            Assert.Equal(0m, result.Reserved); // no reservation entity yet
            Assert.Equal(15m, result.Blocked); // 10 Returns + 5 Quarantine
            Assert.Equal(0m, result.InTransit); // no transfers
            Assert.Equal(20m, result.IncomingQty);
            Assert.NotNull(result.IncomingDate);
            Assert.Equal(DateTimeOffset.UtcNow.AddDays(5).Date, result.IncomingDate.Value.Date);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_100_Availability_BlockedStorageLocation()
    {
        // Arrange - blocked Storage location excluded from eligible
        var blockedLocationId = Guid.NewGuid();
        await using (var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(_dbName), SystemBranchScope.Create()))
        {
            md.StorageLocations.Add(new StorageLocation
            {
                Id = blockedLocationId,
                Code = "LOC-BLOCKED",
                Name = "Blocked Storage",
                Type = "Storage",
                WarehouseId = _warehouse1Id,
                IsActive = true,
                IsBlocked = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await md.SaveChangesAsync();
        }

        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.StockBalances.AddRange(
                new StockBalance
                {
                    ProductId = _productId,
                    WarehouseId = _warehouse1Id,
                    LocationId = _locationStorageId,
                    OnHand = 50m,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                new StockBalance
                {
                    ProductId = _productId,
                    WarehouseId = _warehouse1Id,
                    LocationId = blockedLocationId,
                    OnHand = 30m,
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
            var result = await service.GetAvailabilityAsync(_productId, _warehouse1Id);

            // Assert - blocked Storage location (30) excluded from available
            Assert.Equal(80m, result.OnHand); // 50 + 30
            Assert.Equal(50m, result.Available); // only unblocked Storage
            Assert.Equal(30m, result.Blocked); // blocked Storage counts as blocked
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_100_Availability_InTransit()
    {
        // Arrange - transfer in transit to this warehouse
        var (initScope, _, initInventoryDb) = CreateSystemScope();
        try
        {
            initInventoryDb.Transfers.Add(new Transfer
            {
                TransferId = Guid.NewGuid(),
                SourceWarehouseId = Guid.NewGuid(), // other warehouse
                SourceLocationId = Guid.NewGuid(),
                DestWarehouseId = _warehouse1Id,
                DestLocationId = _locationStorageId,
                ProductId = _productId,
                Qty = 100m,
                ShippedQty = 100m,
                ReceivedQty = 30m,
                Status = Transfer.Statuses.InTransit,
                BranchId = _branch1Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await initInventoryDb.SaveChangesAsync();
        }
        finally
        {
            initScope.Dispose();
        }

        // Act
        var (scope, service, _) = CreateSystemScope();
        try
        {
            var result = await service.GetAvailabilityAsync(_productId, _warehouse1Id);

            // Assert - in-transit = ShippedQty - ReceivedQty = 70
            Assert.Equal(70m, result.InTransit);
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_100_Availability_UnknownWarehouse()
    {
        // Act & Assert - warehouse not found
        var (scope, service, _) = CreateSystemScope();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.GetAvailabilityAsync(_productId, Guid.NewGuid()));
        }
        finally
        {
            scope.Dispose();
        }
    }
}
