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
/// Integration tests for OpeningImport orchestration (A1-7, INV-014).
/// Tests run against throwaway SQL Server databases with real EF Core migrations.
/// Requirement IDs are included in every test name (NFR-051).
/// </summary>
public sealed class OpeningImportOrchestrationTests : IAsyncLifetime
{
    private string _dbName = string.Empty;
    private IServiceProvider _services = null!;

    private Guid _productId;
    private Guid _warehouse1Id;
    private Guid _warehouse2Id;
    private Guid _location1Id;
    private Guid _location2Id;
    private Guid _branch1Id;
    private Guid _branch2Id;

    public async Task InitializeAsync()
    {
        _dbName = ThrowawayDatabase.NewName("InvOpening");
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
        sc.AddScoped<SupplyForge.Inventory.Infrastructure.OpeningImportOrchestrator>();

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
        var warehouse2 = new Warehouse { Id = Guid.NewGuid(), Code = "WH-002", Name = "Warehouse 2", BranchId = branch2.Id, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
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
        _branch2Id = branch2.Id;
    }

    private (IServiceScope Scope, OpeningImportOrchestrator Orchestrator, InventoryDbContext InventoryDb, BranchScopeHolder Holder) CreateSystemScope()
    {
        var scope = _services.CreateScope();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = SystemBranchScope.Create();
        var orchestrator = scope.ServiceProvider.GetRequiredService<OpeningImportOrchestrator>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, orchestrator, inventoryDb, holder);
    }

    private async Task<(IServiceScope Scope, OpeningImportOrchestrator Orchestrator, InventoryDbContext InventoryDb, BranchScopeHolder Holder)> CreateBranchScopeAsync(Guid branchId)
    {
        var scope = _services.CreateScope();
        var provider = scope.ServiceProvider.GetRequiredService<IBranchScopeProvider>();
        var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
        holder.Scope = await provider.ResolveAsync(branchId);
        var orchestrator = scope.ServiceProvider.GetRequiredService<OpeningImportOrchestrator>();
        var inventoryDb = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        return (scope, orchestrator, inventoryDb, holder);
    }

    [Fact]
    public async Task INV_014_OpeningImport_DryRun_NoMovements()
    {
        // Arrange - create Draft opening import
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid importId = await orchestrator.CreateOpeningImportAsync(
                _branch1Id,
                new[]
                {
                    new OpeningImportLineInput(
                        _productId, _warehouse1Id, _location1Id, 100m, 15.00m)
                });

            // Act - dry-run validation
            await orchestrator.ValidateOpeningImportAsync(importId);

            // Assert - status is Validated
            OpeningImport? import = await inventoryDb.OpeningImports.FindAsync([importId]);
            Assert.NotNull(import);
            Assert.Equal(OpeningImport.Statuses.Validated, import.Status);

            // Assert - no StockMovements posted (dry-run)
            var movements = await inventoryDb.StockMovements
                .Where(m => m.SourceDocumentId == importId && m.SourceDocumentType == "OpeningImport")
                .ToListAsync();
            Assert.Empty(movements);

            // Assert - no ProductCost created
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
    public async Task INV_014_OpeningImport_Post_CreatesMovements()
    {
        // Arrange - create and validate opening import
        var (scope, orchestrator, inventoryDb, _) = CreateSystemScope();
        try
        {
            Guid importId = await orchestrator.CreateOpeningImportAsync(
                _branch1Id,
                new[]
                {
                    new OpeningImportLineInput(
                        _productId, _warehouse1Id, _location1Id, 100m, 15.00m)
                });
            await orchestrator.ValidateOpeningImportAsync(importId);

            // Act - post
            await orchestrator.PostOpeningImportAsync(importId);

            // Assert - status is Posted
            OpeningImport? import = await inventoryDb.OpeningImports.FindAsync([importId]);
            Assert.NotNull(import);
            Assert.Equal(OpeningImport.Statuses.Posted, import.Status);

            // Assert - OpeningBalance movement posted
            var movement = await inventoryDb.StockMovements
                .FirstOrDefaultAsync(m => m.SourceDocumentId == importId && m.MovementType == StockMovement.MovementTypes.OpeningBalance);
            Assert.NotNull(movement);
            Assert.Equal(100m, movement.Qty);
            Assert.Equal(15.00m, movement.UnitCost);
            Assert.Equal(_warehouse1Id, movement.WarehouseId);
            Assert.Equal(_location1Id, movement.LocationId);

            // Assert - ProductCost created with WAC
            var productCost = await inventoryDb.ProductCosts
                .FirstOrDefaultAsync(pc => pc.ProductId == _productId && pc.WarehouseId == _warehouse1Id);
            Assert.NotNull(productCost);
            Assert.Equal(100m, productCost.QtyOnHand);
            Assert.Equal(15.00m, Math.Round(productCost.AvgCost, 8));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_014_OpeningImport_CannotPost_WithoutValidation()
    {
        // Arrange - Draft opening import (not validated)
        var (scope, orchestrator, _, _) = CreateSystemScope();
        try
        {
            Guid importId = await orchestrator.CreateOpeningImportAsync(
                _branch1Id,
                new[]
                {
                    new OpeningImportLineInput(
                        _productId, _warehouse1Id, _location1Id, 100m, 15.00m)
                });

            // Act & Assert - cannot post from Draft
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.PostOpeningImportAsync(importId));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_014_OpeningImport_Validate_NonExistentWarehouse_Rejected()
    {
        // Arrange - Draft opening import with non-existent warehouse
        var (scope, orchestrator, _, _) = CreateSystemScope();
        try
        {
            Guid importId = await orchestrator.CreateOpeningImportAsync(
                _branch1Id,
                new[]
                {
                    new OpeningImportLineInput(
                        _productId, Guid.NewGuid(), _location1Id, 100m, 15.00m)
                });

            // Act & Assert - warehouse does not exist
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ValidateOpeningImportAsync(importId));
        }
        finally
        {
            scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_014_OpeningImport_Validate_WrongBranch_Rejected()
    {
        // Arrange - opening import created in branch1
        Guid importId;
        var (createScope, createOrchestrator, _, _) = CreateSystemScope();
        try
        {
            importId = await createOrchestrator.CreateOpeningImportAsync(
                _branch1Id,
                new[]
                {
                    new OpeningImportLineInput(
                        _productId, _warehouse1Id, _location1Id, 100m, 15.00m)
                });
        }
        finally
        {
            createScope.Dispose();
        }

        // Act & Assert - try to validate from branch2 scope
        (IServiceScope branch2Scope, OpeningImportOrchestrator branch2Orchestrator, InventoryDbContext _, BranchScopeHolder _) = await CreateBranchScopeAsync(_branch2Id);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                branch2Orchestrator.ValidateOpeningImportAsync(importId));
        }
        finally
        {
            branch2Scope.Dispose();
        }
    }

    [Fact]
    public async Task INV_014_OpeningImport_Reject_WrongBranch_Rejected()
    {
        // Arrange - opening import created in branch1
        Guid importId;
        var (createScope, createOrchestrator, _, _) = CreateSystemScope();
        try
        {
            importId = await createOrchestrator.CreateOpeningImportAsync(
                _branch1Id,
                new[]
                {
                    new OpeningImportLineInput(
                        _productId, _warehouse1Id, _location1Id, 100m, 15.00m)
                });
        }
        finally
        {
            createScope.Dispose();
        }

        // Act & Assert - try to reject from branch2 scope
        (IServiceScope branch2Scope, OpeningImportOrchestrator branch2Orchestrator, InventoryDbContext _, BranchScopeHolder _) = await CreateBranchScopeAsync(_branch2Id);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                branch2Orchestrator.RejectOpeningImportAsync(importId));
        }
        finally
        {
            branch2Scope.Dispose();
        }
    }
}
