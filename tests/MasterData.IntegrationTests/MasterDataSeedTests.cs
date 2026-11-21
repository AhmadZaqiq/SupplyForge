using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;

namespace SupplyForge.MasterData.IntegrationTests;

/// <summary>
/// A1-1 acceptance for the company singleton and the three-branch seed
/// (MD-001, MD-002), plus the migration boundary for
/// <c>20261119232720_A1InitialMasterData</c>. Every test builds its own
/// database, applies migrations only to that database, and drops it — the
/// development database is never opened (A1 boundary, see
/// <see cref="ThrowawayDatabase"/>).
/// </summary>
public class MasterDataSeedTests
{
    [Fact]
    public async Task MD_001_Seed_CreatesTheCompanySingleton_WithEveryProfileField()
    {
        var dbName = ThrowawayDatabase.NewName("mdcompany");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            var seed = new MasterDataSeed(db, TimeProvider.System);
            Assert.True(await seed.EnsureSeededAsync(), "the migration is applied, so the seed must run");

            var company = await db.Companies.SingleAsync();
            Assert.Equal(Company.SingletonId, company.Id);
            Assert.False(string.IsNullOrWhiteSpace(company.LegalName), "MD-001 legal name");
            Assert.True(company.IsActive);
            Assert.Equal("USD", company.BaseCurrency);
            Assert.Equal("UTC", company.Timezone);
            Assert.Equal(1, company.FiscalYearStartMonth);
            Assert.Equal("en-US", company.DefaultLanguage);

            // MD-001 lists logo as a profile field; the upload/retrieval
            // surface is SEC-40 and waits on ADR-011 (A1-PROGRESS §0 B2), so
            // the column exists and is still empty.
            Assert.Null(company.Logo);
            Assert.Null(company.LogoContentType);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_001_TwoActiveCompanies_AreRejectedByTheSchema()
    {
        var dbName = ThrowawayDatabase.NewName("mdsingleton");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var seeded = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await seeded.GetService<IMigrator>().MigrateAsync();
            Assert.True(await new MasterDataSeed(seeded, TimeProvider.System).EnsureSeededAsync());

            // A second ACTIVE company — MD-001 "exactly one active company
            // row" is a schema invariant, not only a seed convention.
            await using var attempt = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            attempt.Companies.Add(new Company
            {
                Id = Guid.NewGuid(),
                LegalName = "Second Active Company",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => attempt.SaveChangesAsync());

            // Nothing changed: the seed's row is still the only active one.
            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(1, await check.Companies.CountAsync(c => c.IsActive));
            Assert.Equal(Company.SingletonId, (await check.Companies.SingleAsync(c => c.IsActive)).Id);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_002_Seed_CreatesExactlyThreeBranches_WithUniquePlaceholderCodes()
    {
        var dbName = ThrowawayDatabase.NewName("mdbranches");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.True(await new MasterDataSeed(db, TimeProvider.System).EnsureSeededAsync());

            var branches = await db.Branches.OrderBy(b => b.Code).ToListAsync();

            // A-2: seed three — no hard-coded maximum implied by this count.
            Assert.Equal(3, branches.Count);
            Assert.Equal(
                MasterDataSeed.V1Branches.Select(v => v.Code).OrderBy(c => c, StringComparer.Ordinal),
                branches.Select(b => b.Code));

            // Q-3 (branch codes/names) is Open-PO and explicitly accepts
            // placeholders until the product owner answers: assert the shape,
            // not the wording, so the real names drop in without a test change.
            Assert.All(branches, b =>
            {
                Assert.False(string.IsNullOrWhiteSpace(b.Code));
                Assert.False(string.IsNullOrWhiteSpace(b.Name));
                Assert.True(b.IsActive);
            });
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_001_MD_002_SeedRunTwice_CreatesNoDuplicates()
    {
        var dbName = ThrowawayDatabase.NewName("mdidempotent");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using (var first = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName)))
            {
                await first.GetService<IMigrator>().MigrateAsync();
                Assert.True(await new MasterDataSeed(first, TimeProvider.System).EnsureSeededAsync());
            }

            // Second run through a brand-new context — the Host restart case.
            await using (var second = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName)))
            {
                Assert.True(await new MasterDataSeed(second, TimeProvider.System).EnsureSeededAsync());
            }

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(1, await check.Companies.CountAsync());
            Assert.Equal(3, await check.Branches.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_002_DuplicateBranchCode_IsRejectedByTheUniqueIndex()
    {
        var dbName = ThrowawayDatabase.NewName("mdunique");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();
            Assert.True(await new MasterDataSeed(db, TimeProvider.System).EnsureSeededAsync());

            db.Branches.Add(new Branch
            {
                Id = Guid.NewGuid(),
                Code = MasterDataSeed.V1Branches[0].Code,
                Name = "Duplicate of the first seeded branch",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(3, await check.Branches.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_001_SeedWithoutTheMigration_ReportsSchemaAbsent_AndWritesNothing()
    {
        // The Host runs this seed at startup against whatever database it is
        // pointed at. On a database that predates A1 the schema is missing;
        // that must be reported, never crash the Host and never half-seed.
        var dbName = ThrowawayDatabase.NewName("mdnoschema");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.False(await new MasterDataSeed(db, TimeProvider.System).EnsureSeededAsync());

            var tables = await db.Database
                .SqlQueryRaw<int>("SELECT COUNT(*) AS Value FROM sys.tables")
                .SingleAsync();
            Assert.Equal(0, tables);
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MIGRATION_A1InitialMasterData_UpCreatesSchemaTablesAndIndexes_DownReversesExactly()
    {
        var dbName = ThrowawayDatabase.NewName("mdmigration");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));

            await db.GetService<IMigrator>().MigrateAsync();

            // A1-1 owns exactly these two tables. The schema's *total* table
            // count grows with each increment, so it is asserted by the
            // newest migration test (A1-2 asserts six); here the two owned
            // tables are checked by name instead of by a hard-coded total.
            Assert.Equal(2, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data')
                    AND name IN ('Company', 'Branch')").SingleAsync());

            // MD-001: the "exactly one active company" index is unique AND
            // filtered to active rows; MD-002: branch code is globally unique.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.Company')
                    AND name = 'IX_Company_IsActive'
                    AND is_unique = 1
                    AND filter_definition IS NOT NULL").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.Branch')
                    AND name = 'IX_Branch_Code'
                    AND is_unique = 1").SingleAsync());

            // Down() reverses Up() exactly: both owned tables go, and nothing
            // else in the database is touched.
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
}
