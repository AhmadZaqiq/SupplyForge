using Microsoft.EntityFrameworkCore;
using SupplyForge.MasterData.Domain;
using SupplyForge.PlatformCore.Contracts.Security;
using WarehouseEntity = SupplyForge.MasterData.Domain.Warehouse;

namespace SupplyForge.MasterData.Infrastructure.Data;

/// <summary>
/// Master Data module DbContext (ADR-001, ADR-003). Owns the
/// <c>master_data</c> schema: company singleton, branches, the product
/// catalogue (product, category hierarchy, UoM + conversions), the partners
/// + shared master (supplier, customer, addresses, tax rates, payment terms,
/// reason codes), and the warehouse/location catalogue (A1-4). No other
/// module may read or write these tables (MOD-1) — cross-module access goes
/// through this module's public contracts.
/// </summary>
public class MasterDataDbContext : DbContext
{
    public const string Schema = "master_data";

    private readonly ICurrentBranchScope _branchScope;

    public MasterDataDbContext(DbContextOptions<MasterDataDbContext> options, ICurrentBranchScope branchScope)
        : base(options)
    {
        _branchScope = branchScope;
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UnitOfMeasureConversion> UnitOfMeasureConversions => Set<UnitOfMeasureConversion>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SupplierAddress> SupplierAddresses => Set<SupplierAddress>();
    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();
    public DbSet<TaxRate> TaxRates => Set<TaxRate>();
    public DbSet<PaymentTerm> PaymentTerms => Set<PaymentTerm>();
    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();

    // A1-4 — MD-040, MD-041, MD-042
    public DbSet<WarehouseEntity> Warehouses => Set<WarehouseEntity>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceBranchScopeOnWrite();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        EnforceBranchScopeOnWrite();
        return base.SaveChanges();
    }

    /// <summary>
    /// Rejects writes of branch-owned entities outside the caller's scope.
    /// <para>
    /// The StorageLocation check reads <c>Warehouse.BranchId</c> through the
    /// Warehouse navigation, so the Warehouse navigation must be populated
    /// (eagerly loaded) on the tracked StorageLocation; otherwise the branch
    /// id cannot be determined and the write is rejected as out of scope.
    /// </para>
    /// </summary>
    private void EnforceBranchScopeOnWrite()
    {
        var hasBranchOwned = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(e => e.Entity)
            .Any(entity => entity is WarehouseEntity or StorageLocation);
        if (!hasBranchOwned)
        {
            return;
        }

        // Q-E: fail closed — a write under no scope must not create branch data.
        if (_branchScope.IsSystem || _branchScope.IsCompanyWide)
        {
            return;
        }

        foreach (var entry in ChangeTracker.Entries<WarehouseEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            if (!_branchScope.CanAccess(entry.Entity.BranchId))
            {
                throw new InvalidOperationException(
                    "Branch scope rejected: creating or modifying a Warehouse outside the caller's scope (BRN-010).");
            }
        }

        foreach (var entry in ChangeTracker.Entries<StorageLocation>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var branchId = entry.Entity.Warehouse?.BranchId;
            if (branchId is null || !_branchScope.CanAccess(branchId.Value))
            {
                throw new InvalidOperationException(
                    "Branch scope rejected: creating or modifying a StorageLocation outside the caller's scope (BRN-010).");
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("Company");
            e.HasKey(x => x.Id);
            e.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.TaxId).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.LogoContentType).HasMaxLength(100);
            e.Property(x => x.BaseCurrency).HasMaxLength(3).IsRequired();
            e.Property(x => x.Timezone).HasMaxLength(64).IsRequired();
            e.Property(x => x.DefaultLanguage).HasMaxLength(10).IsRequired();
            // MD-001: "exactly one active company row" is enforced by the
            // schema, not only by the seed's get-or-insert.
            e.HasIndex(x => x.IsActive).IsUnique().HasFilter("[IsActive] = 1");
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("Branch");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // MD-002 codes are the human handle for seeds, scoping and Q-3.
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // A1-2 — MD-010/011/012/013 (decisions D-a…D-j in A1-PROGRESS §1a).

        modelBuilder.Entity<Category>(e =>
        {
            e.ToTable("Category");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            // MD-011: self-referencing hierarchy, null ParentId = root.
            // Same rule: a parent with children is deactivated, not deleted.
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<UnitOfMeasure>(e =>
        {
            e.ToTable("UnitOfMeasure");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<UnitOfMeasureConversion>(e =>
        {
            e.ToTable("UnitOfMeasureConversion", t =>
            {
                // D-h: a zero/negative factor silently corrupts every
                // quantity conversion, so the invariant lives in the schema.
                t.HasCheckConstraint("CK_UnitOfMeasureConversion_Factor_Positive", "[Factor] > 0");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.FromUnitId, x.ToUnitId }).IsUnique();
            e.Property(x => x.Factor).HasPrecision(18, 4); // DM-2 qty scale 4
            // Both FKs point at UnitOfMeasure: cascade on both would give SQL
            // Server two cascade paths from one table (error 1753, migration
            // could not be applied). Restrict keeps the data honest — a unit
            // used by a conversion cannot be deleted until the conversion is
            // handled explicitly.
            e.HasOne(x => x.FromUnit).WithMany().HasForeignKey(x => x.FromUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUnit).WithMany().HasForeignKey(x => x.ToUnitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Product");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); // DM-5
            e.Property(x => x.ReorderPoint).HasPrecision(18, 4);     // DM-2 qty
            e.Property(x => x.DefaultPrice).HasPrecision(18, 2);     // DM-2 money, A-1
            e.Property(x => x.StandardCost).HasPrecision(18, 2);     // DM-2 money, A-1
            // "deactivate not delete" (05-02 MD rules): a category or base
            // unit that is in use must be deactivated, never deleted out from
            // under its products — so deletion is blocked, not cascaded.
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BaseUnitOfMeasure).WithMany().HasForeignKey(x => x.BaseUnitOfMeasureId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // A1-3 — MD-020/030/031/050 + PLT-075 (decisions D1…D17 in
        // A1-PROGRESS §1b). Every FK below is Restrict: referenced records
        // deactivate, they are never hard-deleted (PLT-075, MD rules), and
        // EF's convention indexes on the FK columns are deliberately kept.

        modelBuilder.Entity<Supplier>(e =>
        {
            e.ToTable("Supplier");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); // DM-5
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("Customer", t =>
            {
                // Owner answer Q1: NOT NULL with default 0 = "no credit", and
                // P1 has no unlimited value (TD-3 fails closed). A negative
                // limit would silently pre-authorise credit — same rationale
                // as A1-2's Factor > 0.
                t.HasCheckConstraint("CK_Customer_CreditLimit_NonNegative", "[CreditLimit] >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired(); // DM-5
            e.Property(x => x.CreditLimit).HasPrecision(18, 2).HasDefaultValue(0m); // DM-2 money, A-1
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<SupplierAddress>(e =>
        {
            e.ToTable("SupplierAddress");
            e.HasKey(x => x.Id);
            e.Property(x => x.Line1).HasMaxLength(200).IsRequired();
            e.Property(x => x.Line2).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.Region).HasMaxLength(100);
            e.Property(x => x.PostalCode).HasMaxLength(20);
            e.Property(x => x.Country).HasMaxLength(100).IsRequired();
            // Owner answer Q2: at most one default shipping and one default
            // billing address per supplier, enforced by the schema. A filtered
            // unique index is unique only among rows where the flag is 1.
            e.HasIndex(x => new { x.SupplierId, x.IsDefaultShipping }).IsUnique().HasFilter("[IsDefaultShipping] = 1");
            e.HasIndex(x => new { x.SupplierId, x.IsDefaultBilling }).IsUnique().HasFilter("[IsDefaultBilling] = 1");
            e.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<CustomerAddress>(e =>
        {
            e.ToTable("CustomerAddress");
            e.HasKey(x => x.Id);
            e.Property(x => x.Line1).HasMaxLength(200).IsRequired();
            e.Property(x => x.Line2).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100).IsRequired();
            e.Property(x => x.Region).HasMaxLength(100);
            e.Property(x => x.PostalCode).HasMaxLength(20);
            e.Property(x => x.Country).HasMaxLength(100).IsRequired();
            // Owner answer Q2 — same two default indexes as SupplierAddress.
            e.HasIndex(x => new { x.CustomerId, x.IsDefaultShipping }).IsUnique().HasFilter("[IsDefaultShipping] = 1");
            e.HasIndex(x => new { x.CustomerId, x.IsDefaultBilling }).IsUnique().HasFilter("[IsDefaultBilling] = 1");
            e.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<TaxRate>(e =>
        {
            e.ToTable("TaxRate", t =>
            {
                // FIN-CALC-10: a percentage outside 0…100 silently corrupts
                // every tax line it touches.
                t.HasCheckConstraint("CK_TaxRate_Rate_Between0And100", "[Rate] BETWEEN 0 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Rate).HasPrecision(9, 6); // FIN-CALC-10 ~6 dp
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<PaymentTerm>(e =>
        {
            e.ToTable("PaymentTerm", t =>
            {
                // FIN-021 derives due dates from DueDays; a negative day
                // count would move due dates into the past by design.
                t.HasCheckConstraint("CK_PaymentTerm_DueDays_NonNegative", "[DueDays] >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<ReasonCode>(e =>
        {
            e.ToTable("ReasonCode");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // One global list (decision D9): the unique code is the handle
            // every consumer (INV-040, INB-037, …) references.
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // A1-4 — MD-040, MD-041, MD-042 (decisions recorded in A1-PROGRESS §1c).
        // Warehouse: branch-owned, Code unique company-wide (owner decision 5).
        // StorageLocation: branch-owned transitively via Warehouse.BranchId.
        // Both: deactivate not delete (IsActive), RowVersion for optimistic concurrency (DM-7).
        // FKs: Restrict — referenced records deactivate, never hard-deleted (PLT-075, MD rules).
        // PART 2: EF global query filters on Warehouse (BranchId) and
        // StorageLocation (via Warehouse.BranchId). The filter reads the
        // constructor-injected scope (_branchScope), so a fresh context always
        // applies the current scope. System scope sees all; no scope (fail
        // closed, Q-E) sees nothing.

        modelBuilder.Entity<WarehouseEntity>(e =>
        {
            e.ToTable("Warehouse");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // Warehouse.Code is unique company-wide (owner decision 5), not per branch.
            // Non-filtered, matching the A1-3 Code convention: a deactivated warehouse
            // keeps its code reserved (no reuse).
            e.HasIndex(x => x.Code).IsUnique();
            e.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasQueryFilter(x =>
                _branchScope.IsSystem ||
                _branchScope.IsCompanyWide ||
                (_branchScope.BranchIds.Count > 0 && _branchScope.BranchIds.Contains(x.BranchId)));
        });

        modelBuilder.Entity<StorageLocation>(e =>
        {
            e.ToTable("StorageLocation", t =>
            {
                // D-16: only these five types are valid.
                t.HasCheckConstraint("CK_StorageLocation_Type_Valid",
                    "[Type] IN ('Receiving','Quarantine','Storage','Returns','Staging')");
                // MD-042: no "blocked implies inactive" constraint. The PRD only says
                // "Block location; no deactivate while holding stock"; a blocked Storage
                // location can still hold stock and stays active (owner, 2026-11-24).
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(20).IsRequired();
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            // Location.Code unique per warehouse, filtered so a deactivated code is reusable.
            e.HasIndex(x => new { x.WarehouseId, x.Code }).IsUnique().HasFilter("[IsActive] = 1");
            e.HasIndex(x => x.Type); // For eligibility queries (§6.2)
            e.HasOne(x => x.Warehouse).WithMany(x => x.Locations).HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RowVersion).IsRowVersion();
            // Location is branch-owned transitively via Warehouse.BranchId.
            e.HasQueryFilter(x =>
                _branchScope.IsSystem ||
                _branchScope.IsCompanyWide ||
                (_branchScope.BranchIds.Count > 0 && _branchScope.BranchIds.Contains(x.Warehouse!.BranchId)));
        });
    }
}
