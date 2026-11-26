using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.MasterData.IntegrationTests;

/// <summary>
/// A1-2 acceptance for the product catalogue (MD-010, MD-011, MD-012, MD-013
/// persistence half, DM-2, DM-5) and the boundary of migration
/// <c>20261119235652_A1ProductCatalogue</c>. Every test builds its own
/// database, applies migrations only to that database, and drops it — the
/// development database is never opened (see <see cref="ThrowawayDatabase"/>).
/// </summary>
public class ProductCatalogueTests
{
    [Fact]
    public async Task MD_010_Product_PersistsEveryRequiredField()
    {
        var dbName = ThrowawayDatabase.NewName("a1product");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var uom = NewUom("EA");
            var category = NewCategory("CAT-01", "Electronics");
            db.AddRange(uom, category);
            await db.SaveChangesAsync();

            var product = new Product
            {
                Id = Guid.NewGuid(),
                Code = "SKU-0001",
                Name = "Wireless scanner",
                Description = "A1-2 fixture",
                CategoryId = category.Id,
                BaseUnitOfMeasureId = uom.Id,
                RequiresInspection = true,
                ReorderPoint = 12.5000m,
                LeadTimeDays = 14,
                DefaultPrice = 199.99m,
                StandardCost = 120.40m,
                Status = ProductStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Products.Add(product);
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            var loaded = await check.Products.SingleAsync(p => p.Code == "SKU-0001");

            Assert.Equal("Wireless scanner", loaded.Name);
            Assert.Equal("A1-2 fixture", loaded.Description);
            Assert.Equal(category.Id, loaded.CategoryId);
            Assert.Equal(uom.Id, loaded.BaseUnitOfMeasureId);
            Assert.True(loaded.RequiresInspection, "MD-010 RequiresInspection");
            Assert.True(loaded.ReorderPoint == 12.5000m, "MD-010 reorder point, qty scale 4 (DM-2)");
            Assert.True(loaded.LeadTimeDays == 14, "MD-010 lead time");
            Assert.True(loaded.DefaultPrice == 199.99m, "MD-010 default price, money scale 2 (A-1)");
            Assert.True(loaded.StandardCost == 120.40m, "MD-010 standard cost, money scale 2 (A-1)");
            Assert.True(loaded.Status == ProductStatus.Active, "MD-013 status stored as a code (DM-5)");
            Assert.True(ProductStatus.IsValid(loaded.Status), "DM-5: the stored value is a recognised status code");
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_010_ProductCode_IsUnique()
    {
        var dbName = ThrowawayDatabase.NewName("a1sku");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var uom = NewUom("EA");
            db.Add(uom);
            await db.SaveChangesAsync();

            db.Products.Add(NewProduct("SKU-DUP", uom.Id, "First"));
            await db.SaveChangesAsync();

            db.Products.Add(NewProduct("SKU-DUP", uom.Id, "Second"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            Assert.Equal(1, await check.Products.CountAsync(p => p.Code == "SKU-DUP"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_011_CategoryHierarchy_PersistsParentAndChildLinks()
    {
        var dbName = ThrowawayDatabase.NewName("a1cat");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var root = NewCategory("ROOT", "Everything");
            var child = NewCategory("CHILD", "Electronics");
            child.ParentId = root.Id;
            var grandchild = NewCategory("GRAND", "Scanners");
            grandchild.ParentId = child.Id;
            db.AddRange(root, child, grandchild);
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            var loadedRoot = await check.Categories
                .Include(c => c.Children)
                .SingleAsync(c => c.Code == "ROOT");

            Assert.Null(loadedRoot.ParentId);
            var loadedChild = Assert.Single(loadedRoot.Children);
            Assert.Equal("CHILD", loadedChild.Code);

            var loadedGrandchild = await check.Categories.SingleAsync(c => c.Code == "GRAND");
            Assert.Equal(child.Id, loadedGrandchild.ParentId);

            // Any depth, not a fixed two-level structure.
            Assert.Equal(1, await check.Categories.CountAsync(c => c.ParentId == child.Id));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_011_ProductMayBeLeftUncategorised()
    {
        // Decision D-e: the link is nullable, so a product without a category
        // is legal and does not have to invent one.
        var dbName = ThrowawayDatabase.NewName("a1nocat");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var uom = NewUom("EA");
            db.Add(uom);
            await db.SaveChangesAsync();

            var product = NewProduct("SKU-NO-CAT", uom.Id, "No category");
            db.Products.Add(product);
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            var loaded = await check.Products.SingleAsync(p => p.Code == "SKU-NO-CAT");
            Assert.Null(loaded.CategoryId);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_012_UoMConversion_PersistsFactor_AndRejectsASecondPair()
    {
        var dbName = ThrowawayDatabase.NewName("a1uom");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var each = NewUom("EA");
            var box = NewUom("BOX");
            db.AddRange(each, box);
            await db.SaveChangesAsync();

            db.UnitOfMeasureConversions.Add(new UnitOfMeasureConversion
            {
                Id = Guid.NewGuid(),
                FromUnitId = box.Id,
                ToUnitId = each.Id,
                Factor = 12.0000m,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            var loaded = await check.UnitOfMeasureConversions.SingleAsync();
            Assert.Equal(box.Id, loaded.FromUnitId);
            Assert.Equal(each.Id, loaded.ToUnitId);
            Assert.True(loaded.Factor == 12.0000m, "qty scale 4 (DM-2)");

            // The (From, To) pair is unique: one conversion per direction.
            check.UnitOfMeasureConversions.Add(new UnitOfMeasureConversion
            {
                Id = Guid.NewGuid(),
                FromUnitId = box.Id,
                ToUnitId = each.Id,
                Factor = 6.0000m,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => check.SaveChangesAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_012_UoMConversion_NonPositiveFactor_IsRejectedByTheSchema()
    {
        // D-h: the invariant lives in the schema, not only in callers.
        var dbName = ThrowawayDatabase.NewName("a1factor");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var each = NewUom("EA");
            var box = NewUom("BOX");
            db.AddRange(each, box);
            await db.SaveChangesAsync();

            db.UnitOfMeasureConversions.Add(new UnitOfMeasureConversion
            {
                Id = Guid.NewGuid(),
                FromUnitId = box.Id,
                ToUnitId = each.Id,
                Factor = 0m,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            Assert.Equal(0, await check.UnitOfMeasureConversions.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_013_ProductStatus_IsStoredAsStringStatusCode()
    {
        // DM-5: the persisted value is the code itself, readable without a
        // lookup table. (Validation of unknown codes is the domain rule in
        // ProductStatus.IsValid — unit-tested; EF stores whatever string it
        // is given, so the caller must check.)
        var dbName = ThrowawayDatabase.NewName("a1status");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            var uom = NewUom("EA");
            db.Add(uom);
            await db.SaveChangesAsync();

            var product = NewProduct("SKU-ARCH", uom.Id, "To archive");
            product.Status = ProductStatus.Archived;
            db.Products.Add(product);
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            var stored = await check.Products
                .Where(p => p.Code == "SKU-ARCH")
                .Select(p => p.Status)
                .SingleAsync();
            Assert.Equal(ProductStatus.Archived, stored);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task DM_2_CatalogueColumns_AreStoredAtTheirDeclaredPrecision()
    {
        // DM-2 / FIN-CALC-04: qty scale 4, money per base-currency scale 2.
        // Asserted against the live schema, not the EF model, so a drifted
        // migration cannot pass.
        var dbName = ThrowawayDatabase.NewName("a1precision");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.Product')
                    AND name = 'ReorderPoint' AND precision = 18 AND scale = 4").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.Product')
                    AND name = 'DefaultPrice' AND precision = 18 AND scale = 2").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.Product')
                    AND name = 'StandardCost' AND precision = 18 AND scale = 2").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.UnitOfMeasureConversion')
                    AND name = 'Factor' AND precision = 18 AND scale = 4").SingleAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MIGRATION_A1ProductCatalogue_UpCreatesCatalogueTablesAndGuard_DownReversesExactly()
    {
        var dbName = ThrowawayDatabase.NewName("a1catmig");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await db.GetService<IMigrator>().MigrateAsync();

            // A1-1's two tables + A1-2's four catalogue tables, asserted by
            // name so later increments do not break this test — the same
            // de-coupling A1-2 applied to A1-1's test (A1-PROGRESS §6
            // decision 2). The schema total is owned by the newest migration
            // test (A1-3 asserts 13). Nothing here was removed or loosened:
            // all six tables must still exist.
            Assert.Equal(6, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data')
                    AND name IN ('Company', 'Branch', 'Category',
                                 'UnitOfMeasure', 'Product', 'UnitOfMeasureConversion')").SingleAsync());

            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.check_constraints
                  WHERE parent_object_id = OBJECT_ID('master_data.UnitOfMeasureConversion')
                    AND name = 'CK_UnitOfMeasureConversion_Factor_Positive'").SingleAsync());

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

    private static UnitOfMeasure NewUom(string code) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Category NewCategory(string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Product NewProduct(string code, Guid baseUnitId, string name) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        BaseUnitOfMeasureId = baseUnitId,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
