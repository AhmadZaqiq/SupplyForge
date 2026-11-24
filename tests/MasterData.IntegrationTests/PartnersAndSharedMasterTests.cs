using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;

namespace SupplyForge.MasterData.IntegrationTests;

/// <summary>
/// A1-3 acceptance for partners + shared master (MD-020, MD-030, MD-031,
/// MD-050, PLT-075, DM-2, DM-5) and the boundary of migration
/// <c>20261124011532_A1PartnersSharedMaster</c>. Every test builds its own
/// database, applies migrations only to that database, and drops it — the
/// development database is never opened (see <see cref="ThrowawayDatabase"/>).
/// </summary>
public class PartnersAndSharedMasterTests
{
    [Fact]
    public async Task MD_020_Supplier_PersistsEveryRequiredField_AndStatusIsStoredAsAStringCode()
    {
        var dbName = ThrowawayDatabase.NewName("a13sup");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.Suppliers.Add(NewSupplier("SUP-1001", SupplierStatus.Blocked));
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await check.Suppliers.SingleAsync(s => s.Code == "SUP-1001");

            Assert.Equal("SUP-1001", loaded.Code);
            Assert.Equal("SUP-1001", loaded.Name);
            Assert.Equal(SupplierStatus.Blocked, loaded.Status);
            Assert.True(SupplierStatus.IsValid(loaded.Status), "DM-5: the stored value is a recognised status code");
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
    public async Task MD_020_SupplierCode_IsUnique()
    {
        var dbName = ThrowawayDatabase.NewName("a13supdup");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.Suppliers.Add(NewSupplier("SUP-DUP"));
            await db.SaveChangesAsync();

            db.Suppliers.Add(NewSupplier("SUP-DUP"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(1, await check.Suppliers.CountAsync(s => s.Code == "SUP-DUP"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_030_Customer_PersistsCreditLimit_DefaultsToZero_AndIsNotNullableInTheSchema()
    {
        // Owner answer Q1: NOT NULL with default 0; 0 = "no credit allowed";
        // P1 has no unlimited value (TD-3 fails closed).
        var dbName = ThrowawayDatabase.NewName("a13cus");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            // CreditLimit is deliberately not set: the entity default and the
            // database default must both be 0.
            db.Customers.Add(NewCustomer("CUS-2001"));
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await check.Customers.SingleAsync(c => c.Code == "CUS-2001");
            Assert.Equal(0m, loaded.CreditLimit);
            Assert.Equal(CustomerStatus.Active, loaded.Status);
            Assert.True(CustomerStatus.IsValid(loaded.Status), "DM-5");

            // Asserted against the live schema, not the EF model: the column
            // is NOT NULL and carries a database default.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns c
                  JOIN sys.default_constraints dc
                    ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
                  WHERE c.object_id = OBJECT_ID('master_data.Customer')
                    AND c.name = 'CreditLimit' AND c.is_nullable = 0").SingleAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_030_CustomerCode_IsUnique()
    {
        var dbName = ThrowawayDatabase.NewName("a13cusdup");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.Customers.Add(NewCustomer("CUS-DUP"));
            await db.SaveChangesAsync();

            db.Customers.Add(NewCustomer("CUS-DUP"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(1, await check.Customers.CountAsync(c => c.Code == "CUS-DUP"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_030_Customer_NegativeCreditLimit_IsRejectedByTheSchema()
    {
        // A negative limit would silently pre-authorise credit — same
        // rationale as A1-2's Factor > 0: the invariant lives in the schema.
        var dbName = ThrowawayDatabase.NewName("a13cunneg");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.Customers.Add(NewCustomer("CUS-NEG", creditLimit: -1m));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(0, await check.Customers.CountAsync(c => c.Code == "CUS-NEG"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_031_SupplierAddress_PersistsEveryField_AndLinksToItsSupplier()
    {
        var dbName = ThrowawayDatabase.NewName("a13addr");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            var supplier = NewSupplier("SUP-ADDR");
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            db.SupplierAddresses.Add(new SupplierAddress
            {
                Id = Guid.NewGuid(),
                SupplierId = supplier.Id,
                Line1 = "12 Industrial St",
                Line2 = "Gate B",
                City = "Amman",
                Region = "Amman Governorate",
                PostalCode = "11118",
                Country = "Jordan",
                IsDefaultShipping = true,
                IsDefaultBilling = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await check.SupplierAddresses.SingleAsync();
            Assert.Equal(supplier.Id, loaded.SupplierId);
            Assert.Equal("12 Industrial St", loaded.Line1);
            Assert.Equal("Gate B", loaded.Line2);
            Assert.Equal("Amman", loaded.City);
            Assert.Equal("Amman Governorate", loaded.Region);
            Assert.Equal("11118", loaded.PostalCode);
            Assert.Equal("Jordan", loaded.Country);
            Assert.True(loaded.IsDefaultShipping, "MD-031 defaults");
            Assert.True(loaded.IsDefaultBilling, "MD-031 defaults");
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_031_SecondDefaultShippingAddressForTheSameSupplier_IsRejected_WhileNonDefaultsPass()
    {
        // Owner change 3: the filtered unique indexes are proven in both
        // directions — a second default fails, non-defaults are unlimited.
        var dbName = ThrowawayDatabase.NewName("a13flt");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            var supplier = NewSupplier("SUP-FLT");
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();

            // First default shipping address is allowed.
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: true, billing: false, "1 First St"));
            await db.SaveChangesAsync();

            // A second default shipping address for the same supplier must fail.
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: true, billing: false, "2 Second St"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            // Non-default addresses are unlimited.
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: false, billing: false, "3 Third St"));
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: false, billing: false, "4 Fourth St"));
            await db.SaveChangesAsync();

            // Same rule for the billing flag: one default, then reject the second.
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: false, billing: true, "5 Fifth St"));
            await db.SaveChangesAsync();
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: false, billing: true, "6 Sixth St"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(4, await check.SupplierAddresses.CountAsync(a => a.SupplierId == supplier.Id));
            Assert.Equal(1, await check.SupplierAddresses.CountAsync(a => a.SupplierId == supplier.Id && a.IsDefaultShipping));
            Assert.Equal(1, await check.SupplierAddresses.CountAsync(a => a.SupplierId == supplier.Id && a.IsDefaultBilling));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_031_CustomerAddress_DefaultIndexes_AllowOneDefaultPerKind_AndManyNonDefaults()
    {
        var dbName = ThrowawayDatabase.NewName("a13cflt");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            var customer = NewCustomer("CUS-FLT");
            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: true, billing: false, "1 First St"));
            await db.SaveChangesAsync();

            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: true, billing: false, "2 Second St"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: false, billing: false, "3 Third St"));
            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: false, billing: false, "4 Fourth St"));
            await db.SaveChangesAsync();

            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: false, billing: true, "5 Fifth St"));
            await db.SaveChangesAsync();
            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: false, billing: true, "6 Sixth St"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(4, await check.CustomerAddresses.CountAsync(a => a.CustomerId == customer.Id));
            Assert.Equal(1, await check.CustomerAddresses.CountAsync(a => a.CustomerId == customer.Id && a.IsDefaultShipping));
            Assert.Equal(1, await check.CustomerAddresses.CountAsync(a => a.CustomerId == customer.Id && a.IsDefaultBilling));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_050_TaxRate_PersistsRateAtSixDecimalPlaces()
    {
        var dbName = ThrowawayDatabase.NewName("a13tax");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.TaxRates.Add(NewTaxRate("VAT15", "Standard VAT", 15.000000m));
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await check.TaxRates.SingleAsync(t => t.Code == "VAT15");
            Assert.True(loaded.Rate == 15.000000m, "FIN-CALC-10: percentages stored with 6 decimal places");
            Assert.True(loaded.IsActive, "lookup rows default to active");
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_050_TaxRate_RateOutsideZeroTo100_IsRejectedByTheSchema()
    {
        var dbName = ThrowawayDatabase.NewName("a13taxck");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.TaxRates.Add(NewTaxRate("VAT101", "Impossible", 101m));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.TaxRates.Add(NewTaxRate("VATNEG", "Impossible", -1m));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(0, await check.TaxRates.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_050_PaymentTerm_PersistsDueDays()
    {
        var dbName = ThrowawayDatabase.NewName("a13term");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.PaymentTerms.Add(NewPaymentTerm("NET30", "Net 30 days", 30));
            await db.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await check.PaymentTerms.SingleAsync(t => t.Code == "NET30");
            Assert.Equal(30, loaded.DueDays);
            Assert.True(loaded.IsActive, "lookup rows default to active");
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_050_PaymentTerm_NegativeDueDays_IsRejectedByTheSchema()
    {
        var dbName = ThrowawayDatabase.NewName("a13termck");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            // FIN-021 derives due dates from DueDays: a negative count would
            // move every due date into the past by design.
            db.PaymentTerms.Add(NewPaymentTerm("NET-NEG", "Negative", -5));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(0, await check.PaymentTerms.CountAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MD_050_LookupCodes_AreUnique()
    {
        // One global list per lookup (decision D9): the unique code is the
        // handle every consumer references, so a duplicate would make the
        // reference ambiguous.
        var dbName = ThrowawayDatabase.NewName("a13lkdup");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            db.TaxRates.Add(NewTaxRate("DUP", "First", 10m));
            db.PaymentTerms.Add(NewPaymentTerm("DUP", "First", 10));
            db.ReasonCodes.Add(NewReasonCode("DUP", "First"));
            await db.SaveChangesAsync();

            db.TaxRates.Add(NewTaxRate("DUP", "Second", 20m));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.PaymentTerms.Add(NewPaymentTerm("DUP", "Second", 20));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            db.ReasonCodes.Add(NewReasonCode("DUP", "Second"));
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            Assert.Equal(1, await check.TaxRates.CountAsync(t => t.Code == "DUP"));
            Assert.Equal(1, await check.PaymentTerms.CountAsync(t => t.Code == "DUP"));
            Assert.Equal(1, await check.ReasonCodes.CountAsync(r => r.Code == "DUP"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task PLT_075_SupplierWithAddresses_CannotBeHardDeleted_MustBeDeactivated()
    {
        var dbName = ThrowawayDatabase.NewName("a13del");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            var supplier = NewSupplier("SUP-DEL");
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            db.SupplierAddresses.Add(NewSupplierAddress(supplier.Id, shipping: false, billing: false, "1 St"));
            await db.SaveChangesAsync();

            // Hard delete must be refused while a child row exists (FK Restrict).
            await using var del = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await del.Suppliers.SingleAsync(s => s.Code == "SUP-DEL");
            del.Suppliers.Remove(loaded);
            await Assert.ThrowsAsync<DbUpdateException>(() => del.SaveChangesAsync());

            // The approved path: deactivate, never delete (PLT-075).
            await using var deact = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var stillThere = await deact.Suppliers.SingleAsync(s => s.Code == "SUP-DEL");
            stillThere.Status = SupplierStatus.Inactive;
            stillThere.UpdatedAt = DateTimeOffset.UtcNow;
            await deact.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var final = await check.Suppliers.SingleAsync(s => s.Code == "SUP-DEL");
            Assert.Equal(SupplierStatus.Inactive, final.Status);
            Assert.Equal(1, await check.SupplierAddresses.CountAsync(a => a.SupplierId == final.Id));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task PLT_075_CustomerWithAddresses_CannotBeHardDeleted_MustBeDeactivated()
    {
        var dbName = ThrowawayDatabase.NewName("a13cdel");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            var customer = NewCustomer("CUS-DEL");
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            db.CustomerAddresses.Add(NewCustomerAddress(customer.Id, shipping: false, billing: false, "1 St"));
            await db.SaveChangesAsync();

            await using var del = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var loaded = await del.Customers.SingleAsync(c => c.Code == "CUS-DEL");
            del.Customers.Remove(loaded);
            await Assert.ThrowsAsync<DbUpdateException>(() => del.SaveChangesAsync());

            await using var deact = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var stillThere = await deact.Customers.SingleAsync(c => c.Code == "CUS-DEL");
            stillThere.Status = CustomerStatus.Inactive;
            stillThere.UpdatedAt = DateTimeOffset.UtcNow;
            await deact.SaveChangesAsync();

            await using var check = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            var final = await check.Customers.SingleAsync(c => c.Code == "CUS-DEL");
            Assert.Equal(CustomerStatus.Inactive, final.Status);
            Assert.Equal(1, await check.CustomerAddresses.CountAsync(a => a.CustomerId == final.Id));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task DM_2_A13Columns_AreStoredAtTheirDeclaredPrecision()
    {
        // DM-2 / FIN-CALC-10: money at base-currency scale 2, percentages
        // ~6 dp. Asserted against the live schema, not the EF model, so a
        // drifted migration cannot pass.
        var dbName = ThrowawayDatabase.NewName("a13prec");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.Customer')
                    AND name = 'CreditLimit' AND precision = 18 AND scale = 2").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.columns
                  WHERE object_id = OBJECT_ID('master_data.TaxRate')
                    AND name = 'Rate' AND precision = 9 AND scale = 6").SingleAsync());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MIGRATION_A1PartnersSharedMaster_UpCreatesSevenTablesAndThreeChecks_DownReversesExactly()
    {
        var dbName = ThrowawayDatabase.NewName("a13mig");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var db = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName));
            await db.GetService<IMigrator>().MigrateAsync();

            // A1-3's own seven tables, by name. The schema total is owned by
            // the newest migration test (A1-4 asserts 15); this test was
            // de-coupled from later increments the same way A1-2 de-coupled
            // A1-1 and A1-3 de-coupled A1-2. Nothing was removed.
            Assert.Equal(7, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.tables
                  WHERE schema_id = SCHEMA_ID('master_data')
                    AND name IN ('Supplier','Customer','SupplierAddress',
                                 'CustomerAddress','TaxRate','PaymentTerm','ReasonCode')").SingleAsync());

            // The three CHECK constraints added by this increment.
            Assert.Equal(3, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.check_constraints
                  WHERE parent_object_id IN (
                        OBJECT_ID('master_data.Customer'),
                        OBJECT_ID('master_data.TaxRate'),
                        OBJECT_ID('master_data.PaymentTerm'))
                    AND name IN ('CK_Customer_CreditLimit_NonNegative',
                                 'CK_TaxRate_Rate_Between0And100',
                                 'CK_PaymentTerm_DueDays_NonNegative')").SingleAsync());

            // The four filtered unique default-address indexes (owner answer Q2).
            Assert.Equal(4, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id IN (OBJECT_ID('master_data.SupplierAddress'),
                                      OBJECT_ID('master_data.CustomerAddress'))
                    AND is_unique = 1 AND filter_definition IS NOT NULL").SingleAsync());

            // EF's convention indexes on FK columns were not suppressed: the
            // address FK columns are indexed by the default composite indexes
            // whose leftmost column is the FK (SQL Server seeks those for
            // parent lookups), and the pre-existing convention indexes from
            // A1-2 are still present.
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.SupplierAddress')
                    AND name = 'IX_SupplierAddress_SupplierId_IsDefaultShipping'").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('master_data.Product')
                    AND name = 'IX_Product_CategoryId'").SingleAsync());

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

    private static Supplier NewSupplier(string code, string status = SupplierStatus.Active) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Status = status,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Customer NewCustomer(string code, decimal? creditLimit = null)
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        if (creditLimit is not null)
        {
            customer.CreditLimit = creditLimit.Value;
        }

        return customer;
    }

    private static SupplierAddress NewSupplierAddress(Guid supplierId, bool shipping, bool billing, string line1) => new()
    {
        Id = Guid.NewGuid(),
        SupplierId = supplierId,
        Line1 = line1,
        City = "Amman",
        Country = "Jordan",
        IsDefaultShipping = shipping,
        IsDefaultBilling = billing,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static CustomerAddress NewCustomerAddress(Guid customerId, bool shipping, bool billing, string line1) => new()
    {
        Id = Guid.NewGuid(),
        CustomerId = customerId,
        Line1 = line1,
        City = "Amman",
        Country = "Jordan",
        IsDefaultShipping = shipping,
        IsDefaultBilling = billing,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static TaxRate NewTaxRate(string code, string name, decimal rate) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        Rate = rate,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static PaymentTerm NewPaymentTerm(string code, string name, int dueDays) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        DueDays = dueDays,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ReasonCode NewReasonCode(string code, string name) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
