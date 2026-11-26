using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.MasterData.Infrastructure.Location;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.MasterData.IntegrationTests;

/// <summary>
/// A1-4 acceptance for warehouses + storage locations (MD-040, MD-041,
/// MD-042, DM-1, DM-3, DM-5, DM-7, PLT-075, D-16, §6.2) and the boundary
/// of migration <c>A1WarehousesLocations</c>. Every test builds its own
/// database, applies migrations only to that database, and drops it — the
/// development database is never opened (see <see cref="ThrowawayDatabase"/>).
/// </summary>
public class WarehouseLocationTests
{
    [Fact]
    public async Task MD_040_Warehouse_PersistsEveryField()
    {
        var dbName = ThrowawayDatabase.NewName("a14wh");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);

            var warehouse = new Warehouse
            {
                Id = Guid.NewGuid(),
                BranchId = branchId,
                Code = "WH-001",
                Name = "Main Warehouse",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Warehouses.Add(warehouse);
            await db.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var loaded = await check.Warehouses.SingleAsync(w => w.Code == "WH-001");

            Assert.Equal(branchId, loaded.BranchId);
            Assert.Equal("WH-001", loaded.Code);
            Assert.Equal("Main Warehouse", loaded.Name);
            Assert.True(loaded.IsActive);
            Assert.NotEqual(Guid.Empty, loaded.Id);
            Assert.True(loaded.CreatedAt <= DateTimeOffset.UtcNow);
            Assert.Null(loaded.UpdatedAt);
            Assert.NotEmpty(loaded.RowVersion);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_040_Warehouse_Code_UniqueCompanyWide_NonFiltered()
    {
        var dbName = ThrowawayDatabase.NewName("a14whdup");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branch1 = Guid.NewGuid();
            var branch2 = Guid.NewGuid();
            await AddBranchAsync(db, branch1);
            await AddBranchAsync(db, branch2);

            db.Warehouses.Add(new Warehouse { Id = Guid.NewGuid(), BranchId = branch1, Code = "WH-001", Name = "W1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            // Same code, different branch — rejected (unique company-wide).
            db.Warehouses.Add(new Warehouse { Id = Guid.NewGuid(), BranchId = branch2, Code = "WH-001", Name = "W2", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // Non-filtered: even an INACTIVE row with the same code is rejected,
            // so a deactivated warehouse keeps its code reserved (owner Q-A).
            db.Warehouses.Add(new Warehouse { Id = Guid.NewGuid(), BranchId = branch2, Code = "WH-001", Name = "W2-Inactive", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = NewContext(dbName);
            Assert.Equal(1, await check.Warehouses.CountAsync(w => w.Code == "WH-001"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_040_Warehouse_BranchId_FKRestrict_DeactivateNotDelete()
    {
        var dbName = ThrowawayDatabase.NewName("a14whdel");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);

            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-DEL", Name = "To Delete", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-01", Name = "Child", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            // Restrict blocks deleting a parent that still has children.
            await using var delWh = NewContext(dbName);
            var loadedWh = await delWh.Warehouses.SingleAsync(w => w.Code == "WH-DEL");
            delWh.Warehouses.Remove(loadedWh);
            await Assert.ThrowsAsync<DbUpdateException>(() => delWh.SaveChangesAsync());

            // Restrict also blocks deleting the branch that owns the warehouse.
            await using var delBranch = NewContext(dbName);
            var loadedBranch = await delBranch.Branches.SingleAsync(b => b.Id == branchId);
            delBranch.Branches.Remove(loadedBranch);
            await Assert.ThrowsAsync<DbUpdateException>(() => delBranch.SaveChangesAsync());

            // The approved path: deactivate, never delete (PLT-075, MD rules).
            await using var deact = NewContext(dbName);
            var stillThere = await deact.Warehouses.SingleAsync(w => w.Code == "WH-DEL");
            stillThere.IsActive = false;
            stillThere.UpdatedAt = DateTimeOffset.UtcNow;
            await deact.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var final = await check.Warehouses.SingleAsync(w => w.Code == "WH-DEL");
            Assert.False(final.IsActive);
            Assert.NotNull(final.UpdatedAt);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_041_StorageLocation_AllFiveTypesPersisted()
    {
        var dbName = ThrowawayDatabase.NewName("a14loctype");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-LOC", Name = "WH for Locations", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            foreach (var type in LocationType.All)
            {
                db.StorageLocations.Add(new StorageLocation
                {
                    Id = Guid.NewGuid(),
                    WarehouseId = wh.Id,
                    Type = type,
                    Code = $"{type}-01",
                    Name = type,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }
            await db.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var loaded = await check.StorageLocations.Where(l => l.WarehouseId == wh.Id).ToListAsync();
            Assert.Equal(5, loaded.Count);
            Assert.Contains(loaded, l => l.Type == LocationType.Receiving);
            Assert.Contains(loaded, l => l.Type == LocationType.Quarantine);
            Assert.Contains(loaded, l => l.Type == LocationType.Storage);
            Assert.Contains(loaded, l => l.Type == LocationType.Returns);
            Assert.Contains(loaded, l => l.Type == LocationType.Staging);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_041_StorageLocation_InvalidType_RejectedByCheckConstraint()
    {
        var dbName = ThrowawayDatabase.NewName("a14lck");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-CK", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            db.StorageLocations.Add(new StorageLocation
            {
                Id = Guid.NewGuid(),
                WarehouseId = wh.Id,
                Type = "InvalidType",
                Code = "X-01",
                Name = "Invalid",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.StorageLocations.Add(new StorageLocation
            {
                Id = Guid.NewGuid(),
                WarehouseId = wh.Id,
                Type = "Picking",
                Code = "X-02",
                Name = "Not a type",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = NewContext(dbName);
            Assert.Equal(0, await check.StorageLocations.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_041_StorageLocation_Code_UniquePerWarehouse_FilteredIndex()
    {
        var dbName = ThrowawayDatabase.NewName("a14locdup");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-LOC2", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "A-01", Name = "Loc 1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            // Same code, same warehouse, active — rejected.
            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "A-01", Name = "Loc 2", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // Inactive duplicate is allowed (filtered index).
            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "A-01", Name = "Loc 1 Inactive", IsActive = false, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            await using var check = NewContext(dbName);
            Assert.Equal(2, await check.StorageLocations.CountAsync(l => l.WarehouseId == wh.Id && l.Code == "A-01"));
            Assert.Equal(1, await check.StorageLocations.CountAsync(l => l.WarehouseId == wh.Id && l.Code == "A-01" && l.IsActive));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_042_StorageLocation_BlockedDefaultsFalse_AndBlockedCanStayActive()
    {
        var dbName = ThrowawayDatabase.NewName("a14blk");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-BLK", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            // Default IsBlocked = false.
            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-01", Name = "Normal", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            // Blocked + Active is legal: a blocked Storage location can hold stock
            // and stays active (owner, 2026-11-24; no PRD rule says otherwise).
            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-02", Name = "Blocked Active", IsActive = true, IsBlocked = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var normal = await check.StorageLocations.SingleAsync(l => l.Code == "L-01");
            Assert.False(normal.IsBlocked);
            var blocked = await check.StorageLocations.SingleAsync(l => l.Code == "L-02");
            Assert.True(blocked.IsBlocked);
            Assert.True(blocked.IsActive);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_042_StorageLocation_DeactivateWhileHoldingStock_PortPlaceholderAllows()
    {
        // The real guard is ILocationDeactivationGuard; in A1-4 the placeholder
        // allows everything. A1-5 (Inventory) replaces it with a StockBalance check.
        var guard = new DefaultLocationDeactivationGuard();
        Assert.True(await guard.CanDeactivateAsync(Guid.NewGuid()));

        var dbName = ThrowawayDatabase.NewName("a14deact");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-DEACT", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-01", Name = "Active", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            await using var deact = NewContext(dbName);
            var loc = await deact.StorageLocations.SingleAsync(l => l.Code == "L-01");
            loc.IsActive = false;
            loc.UpdatedAt = DateTimeOffset.UtcNow;
            await deact.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var final = await check.StorageLocations.SingleAsync(l => l.Code == "L-01");
            Assert.False(final.IsActive);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task DM_1_WarehouseStorageLocation_TransitiveBranchOwnership()
    {
        var dbName = ThrowawayDatabase.NewName("a14dm1");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-DM1", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            await db.SaveChangesAsync();

            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-01", Name = "Loc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var loadedWh = await check.Warehouses.SingleAsync(w => w.Code == "WH-DM1");
            var loadedLoc = await check.StorageLocations.SingleAsync(l => l.Code == "L-01");

            Assert.Equal(branchId, loadedWh.BranchId);
            Assert.Equal(loadedWh.Id, loadedLoc.WarehouseId);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task DM_3_WarehouseStorageLocation_SurrogateKeys_AreGuid()
    {
        var dbName = ThrowawayDatabase.NewName("a14dm3");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-DM3", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-01", Name = "Loc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.Warehouse')
                    AND name = 'Id' AND system_type_id = TYPE_ID('uniqueidentifier')").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.StorageLocation')
                    AND name = 'Id' AND system_type_id = TYPE_ID('uniqueidentifier')").SingleAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task DM_7_RowVersion_IsPresent_AndStaleWriteFails()
    {
        var dbName = ThrowawayDatabase.NewName("a14dm7");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            var branchId = Guid.NewGuid();
            await AddBranchAsync(db, branchId);
            var wh = new Warehouse { Id = Guid.NewGuid(), BranchId = branchId, Code = "WH-RV", Name = "WH", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Warehouses.Add(wh);
            db.StorageLocations.Add(new StorageLocation { Id = Guid.NewGuid(), WarehouseId = wh.Id, Type = LocationType.Storage, Code = "L-01", Name = "Loc", IsActive = true, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            await using var check = NewContext(dbName);
            var loadedWh = await check.Warehouses.SingleAsync(w => w.Code == "WH-RV");
            var loadedLoc = await check.StorageLocations.SingleAsync(l => l.Code == "L-01");
            Assert.NotEmpty(loadedWh.RowVersion);
            Assert.NotEmpty(loadedLoc.RowVersion);

            // Both contexts load BEFORE either saves, so update2 holds a stale RowVersion.
            await using var update1 = NewContext(dbName);
            var wh1 = await update1.Warehouses.SingleAsync(w => w.Code == "WH-RV");
            await using var update2 = NewContext(dbName);
            var wh2 = await update2.Warehouses.SingleAsync(w => w.Code == "WH-RV");

            wh1.Name = "Updated 1";
            wh1.UpdatedAt = DateTimeOffset.UtcNow;
            await update1.SaveChangesAsync();

            wh2.Name = "Updated 2 (stale)";
            wh2.UpdatedAt = DateTimeOffset.UtcNow;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => update2.SaveChangesAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MIGRATION_A1WarehousesLocations_UpCreatesTablesAndGuards_DownReversesExactly()
    {
        var dbName = ThrowawayDatabase.NewName("a14mig");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = NewContext(dbName);
            await db.GetService<IMigrator>().MigrateAsync();

            // A1-1 (2) + A1-2 (4) + A1-3 (7) + A1-4 (2) = 15 tables in master_data
            // (EF's own history table excluded).
            Assert.Equal(15, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data')
                    AND name <> '__EFMigrationsHistory'").SingleAsync());

            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data') AND name = 'Warehouse'").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data') AND name = 'StorageLocation'").SingleAsync());

            // Only the type CHECK exists; there is deliberately NO blocked=>inactive check.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.check_constraints
                  WHERE parent_object_id = OBJECT_ID('master_data.StorageLocation')
                    AND name = 'CK_StorageLocation_Type_Valid'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.check_constraints
                  WHERE parent_object_id = OBJECT_ID('master_data.StorageLocation')
                    AND name LIKE '%Blocked%'").SingleAsync());

            // Warehouse.Code: unique, NOT filtered (owner Q-A).
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.Warehouse')
                    AND name = 'IX_Warehouse_Code' AND is_unique = 1
                    AND (filter_definition IS NULL OR filter_definition = '')").SingleAsync());

            // StorageLocation.Code: unique per warehouse, filtered IsActive=1.
            // SQL Server normalises the filter text (no spaces), so match on IsActive.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.StorageLocation')
                    AND name = 'IX_StorageLocation_WarehouseId_Code' AND is_unique = 1
                    AND filter_definition LIKE '%IsActive%'").SingleAsync());

            // FK indexes (EF convention, Restrict). The StorageLocation FK is
            // covered by the leftmost column of the composite unique index, so EF
            // emits no separate single-column index (same rule as A1-3).
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.Warehouse')
                    AND name = 'IX_Warehouse_BranchId'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.StorageLocation')
                    AND name = 'IX_StorageLocation_WarehouseId'").SingleAsync());

            // Type index for §6.2 eligibility queries.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.StorageLocation')
                    AND name = 'IX_StorageLocation_Type'").SingleAsync());

            // Down() reverses the whole history: every master_data table goes.
            await db.GetService<IMigrator>().MigrateAsync("0");
            Assert.Equal(0, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data')
                    AND name <> '__EFMigrationsHistory'").SingleAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    private static MasterDataDbContext NewContext(string dbName) =>
        new(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());

    private static async Task AddBranchAsync(MasterDataDbContext db, Guid branchId)
    {
        db.Branches.Add(new Branch
        {
            Id = branchId,
            Code = $"BR-{branchId:N}"[..20],
            Name = "Branch",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }
}