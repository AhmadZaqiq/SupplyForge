using Microsoft.EntityFrameworkCore;
using SupplyForge.MasterData.Domain;

namespace SupplyForge.MasterData.Infrastructure.Data;

/// <summary>
/// Master Data module DbContext (ADR-001, ADR-003). Owns the
/// <c>master_data</c> schema: company singleton, branches, and the product
/// catalogue (product, category hierarchy, UoM + conversions); partners and
/// the warehouse/location catalogue arrive in later A1 increments. No other
/// module may read or write these tables (MOD-1) — cross-module access goes
/// through this module's public contracts.
/// </summary>
public class MasterDataDbContext : DbContext
{
    public const string Schema = "master_data";

    public MasterDataDbContext(DbContextOptions<MasterDataDbContext> options)
        : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();
    public DbSet<UnitOfMeasureConversion> UnitOfMeasureConversions => Set<UnitOfMeasureConversion>();

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
    }
}
