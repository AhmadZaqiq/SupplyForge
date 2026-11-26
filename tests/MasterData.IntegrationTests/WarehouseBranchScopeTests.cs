using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.MasterData.IntegrationTests;

/// <summary>
/// A1-4 Part 2 acceptance for branch scoping (BRN-010, BRN-012, MD-040,
/// MD-041, Q-D, Q-E option a). Every test builds its own database, applies
/// migrations only to that database, and drops it — the development database
/// is never opened (see <see cref="ThrowawayDatabase"/>).
/// </summary>
public class WarehouseBranchScopeTests
{
    private static readonly ICurrentBranchScope SystemScope = SystemBranchScope.Create();
    [Fact]
    public async Task BRN_012_Warehouse_BranchA_CannotReadBranchB_ByIdOrList()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2read");
        try
        {
            await using var seed = CreateContext(dbName, SystemScope);
            var whA = NewWarehouse(branchA, "WH-A");
            var whB = NewWarehouse(branchB, "WH-B");
            seed.Warehouses.AddRange(whA, whB);
            await seed.SaveChangesAsync();

            await using var dbA = CreateContext(dbName, Scope(branchA));
            Assert.Equal(whA.Id, (await dbA.Warehouses.SingleAsync()).Id);
            Assert.DoesNotContain(await dbA.Warehouses.ToListAsync(), w => w.Id == whB.Id);
            Assert.Null(await dbA.Warehouses.SingleOrDefaultAsync(w => w.Id == whB.Id));

            await using var dbB = CreateContext(dbName, Scope(branchB));
            Assert.DoesNotContain(await dbB.Warehouses.ToListAsync(), w => w.Id == whA.Id);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task BRN_012_StorageLocation_BranchA_CannotReadBranchB_ByIdOrList()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2loc");
        try
        {
            await using var seed = CreateContext(dbName, SystemScope);
            var whA = NewWarehouse(branchA, "WH-A");
            var whB = NewWarehouse(branchB, "WH-B");
            seed.Warehouses.AddRange(whA, whB);
            await seed.SaveChangesAsync();
            var locA = NewLocation(whA.Id, "L-A");
            var locB = NewLocation(whB.Id, "L-B");
            seed.StorageLocations.AddRange(locA, locB);
            await seed.SaveChangesAsync();

            await using var dbA = CreateContext(dbName, Scope(branchA));
            Assert.Equal(locA.Id, (await dbA.StorageLocations.SingleAsync()).Id);
            Assert.DoesNotContain(await dbA.StorageLocations.ToListAsync(), l => l.Id == locB.Id);
            Assert.Null(await dbA.StorageLocations.SingleOrDefaultAsync(l => l.Id == locB.Id));

            await using var dbB = CreateContext(dbName, Scope(branchB));
            Assert.DoesNotContain(await dbB.StorageLocations.ToListAsync(), l => l.Id == locA.Id);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task BRN_010_CompanyWide_SeesAllBranches()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2cw");
        try
        {
            await using var seed = CreateContext(dbName, SystemScope);
            seed.Warehouses.AddRange(NewWarehouse(branchA, "WH-A"), NewWarehouse(branchB, "WH-B"));
            await seed.SaveChangesAsync();

            await using var db = CreateContext(dbName, CompanyWideScope());
            Assert.Equal(2, await db.Warehouses.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task BRN_010_UserWithNoBranches_SeesNothing()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2none");
        try
        {
            await using var seed = CreateContext(dbName, SystemScope);
            seed.Warehouses.AddRange(NewWarehouse(branchA, "WH-A"), NewWarehouse(branchB, "WH-B"));
            await seed.SaveChangesAsync();

            await using var db = CreateContext(dbName, NoBranchScope.Instance);
            Assert.Empty(await db.Warehouses.ToListAsync());
            Assert.Empty(await db.StorageLocations.ToListAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task Q_E_NoScope_FailsClosed_SeesNothingAndCreatesNothing()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2fc");
        try
        {
            await using var seed = CreateContext(dbName, SystemScope);
            seed.Warehouses.AddRange(NewWarehouse(branchA, "WH-A"), NewWarehouse(branchB, "WH-B"));
            await seed.SaveChangesAsync();

            // Default context (no scope set) = fail closed.
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), BranchScopeDefaults.NoScope);
            Assert.Empty(await db.Warehouses.ToListAsync());

            db.Warehouses.Add(NewWarehouse(branchA, "WH-NEW"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_040_CreateWarehouse_OutsideCallerScope_IsRejected()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2create");
        try
        {
            await using var db = CreateContext(dbName, Scope(branchA));
            db.Warehouses.Add(NewWarehouse(branchB, "WH-B"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

            db.ChangeTracker.Clear();
            db.Warehouses.Add(NewWarehouse(branchA, "WH-A"));
            await db.SaveChangesAsync();
            Assert.Equal(1, await db.Warehouses.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task SystemScope_SeesAllBranches_AndCanCreate()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2sys");
        try
        {
            await using var db = CreateContext(dbName, SystemScope);
            db.Warehouses.AddRange(NewWarehouse(branchA, "WH-A"), NewWarehouse(branchB, "WH-B"));
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.Warehouses.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task BRN_012_TwoConcurrentScopes_EachSeesOnlyItsOwnBranch()
    {
        var (dbName, branchA, branchB) = await SeedTwoBranchesAsync("a14p2conc");
        try
        {
            await using var seed = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await seed.GetService<IMigrator>().MigrateAsync();

            var whA = NewWarehouse(branchA, "WH-A");
            var whB = NewWarehouse(branchB, "WH-B");
            var locA = NewLocation(whA.Id, "L-A");
            var locB = NewLocation(whB.Id, "L-B");
            seed.Warehouses.AddRange(whA, whB);
            seed.StorageLocations.AddRange(locA, locB);
            await seed.SaveChangesAsync();

            var services = new ServiceCollection()
                .AddDbContext<MasterDataDbContext>(o => o.UseSqlServer(
                    ThrowawayDatabase.For(dbName),
                    sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema)))
                .AddScoped<BranchScopeHolder>()
                .AddScoped<ICurrentBranchScope>(sp => sp.GetRequiredService<BranchScopeHolder>().Scope)
                .BuildServiceProvider();

            async Task<List<Warehouse>> ReadWarehouses(Guid branchId)
            {
                using var scope = services.CreateScope();
                var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
                holder.Scope = new RequestBranchScope(new[] { branchId }, isCompanyWide: false);
                var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
                return await db.Warehouses.ToListAsync();
            }

            async Task<List<StorageLocation>> ReadLocations(Guid branchId)
            {
                using var scope = services.CreateScope();
                var holder = scope.ServiceProvider.GetRequiredService<BranchScopeHolder>();
                holder.Scope = new RequestBranchScope(new[] { branchId }, isCompanyWide: false);
                var db = scope.ServiceProvider.GetRequiredService<MasterDataDbContext>();
                return await db.StorageLocations.ToListAsync();
            }

            var tWhA = ReadWarehouses(branchA);
            var tWhB = ReadWarehouses(branchB);
            var tLocA = ReadLocations(branchA);
            var tLocB = ReadLocations(branchB);
            await Task.WhenAll(new Task[] { tWhA, tWhB, tLocA, tLocB });

            var warehousesA = await tWhA;
            var warehousesB = await tWhB;
            var locationsA = await tLocA;
            var locationsB = await tLocB;

            Assert.Single(warehousesA);
            Assert.Equal(branchA, warehousesA[0].BranchId);
            Assert.Single(warehousesB);
            Assert.Equal(branchB, warehousesB[0].BranchId);
            Assert.Single(locationsA);
            Assert.Equal(locA.Id, locationsA[0].Id);
            Assert.Single(locationsB);
            Assert.Equal(locB.Id, locationsB[0].Id);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    private static MasterDataDbContext CreateContext(string dbName, ICurrentBranchScope scope) =>
        new(ThrowawayDatabase.MasterData(dbName), scope);

    private static RequestBranchScope Scope(Guid branchId) =>
        new(new[] { branchId }, isCompanyWide: false);

    private static RequestBranchScope CompanyWideScope() =>
        new(Array.Empty<Guid>(), isCompanyWide: true);

    private static Warehouse NewWarehouse(Guid branchId, string code) =>
        new()
        {
            Id = Guid.NewGuid(),
            BranchId = branchId,
            Code = code,
            Name = code,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static StorageLocation NewLocation(Guid warehouseId, string code) =>
        new()
        {
            Id = Guid.NewGuid(),
            WarehouseId = warehouseId,
            Type = LocationType.Storage,
            Code = code,
            Name = code,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    private static async Task<(string DbName, Guid BranchA, Guid BranchB)> SeedTwoBranchesAsync(string prefix)
    {
        var dbName = ThrowawayDatabase.NewName(prefix);
        await ThrowawayDatabase.CreateAsync(dbName);
        await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
        await db.GetService<IMigrator>().MigrateAsync();

        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        db.Branches.AddRange(
            new Branch { Id = branchA, Code = $"BR-{branchA:N}"[..20], Name = "Branch A", IsActive = true, CreatedAt = DateTimeOffset.UtcNow },
            new Branch { Id = branchB, Code = $"BR-{branchB:N}"[..20], Name = "Branch B", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return (dbName, branchA, branchB);
    }
}