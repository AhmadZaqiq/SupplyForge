using Microsoft.EntityFrameworkCore;
using SupplyForge.Inventory.Domain;
using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.Inventory.Infrastructure.Data;

public class InventoryDbContext : DbContext
{
    public const string Schema = "inventory";

    private readonly ICurrentBranchScope _branchScope;

    public InventoryDbContext(DbContextOptions<InventoryDbContext> options, ICurrentBranchScope branchScope)
        : base(options)
    {
        _branchScope = branchScope;
    }

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<ProductCost> ProductCosts => Set<ProductCost>();

    // A1-7: transfer / adjustment / opening import documents
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<Adjustment> Adjustments => Set<Adjustment>();
    public DbSet<AdjustmentLine> AdjustmentLines => Set<AdjustmentLine>();
    public DbSet<OpeningImport> OpeningImports => Set<OpeningImport>();
    public DbSet<OpeningImportLine> OpeningImportLines => Set<OpeningImportLine>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        RejectStockMovementMutations();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RejectStockMovementMutations();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges()
    {
        RejectStockMovementMutations();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RejectStockMovementMutations();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void RejectStockMovementMutations()
    {
        // StockMovement is INSERT-only: reject Modified/Deleted (INV-010, INV-012).
        foreach (var entry in ChangeTracker.Entries<StockMovement>()
            .Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException(
                "StockMovement is immutable: update and delete are not allowed (INV-010, INV-012).");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<StockMovement>(e =>
        {
            e.ToTable("StockMovement");
            e.HasKey(x => x.MovementId);
            e.Property(x => x.MovementType).HasMaxLength(30).IsRequired();
            e.Property(x => x.Qty).HasPrecision(18, 4);
            e.Property(x => x.UnitCost).HasPrecision(18, 8);
            e.Property(x => x.SourceDocumentType).HasMaxLength(50).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.ProductId, x.WarehouseId, x.LocationId });
            e.HasIndex(x => new { x.MovementType, x.CreatedAt });
        });

        modelBuilder.Entity<StockBalance>(e =>
        {
            e.ToTable("StockBalance", t =>
            {
                t.HasCheckConstraint("CK_StockBalance_OnHand_NonNegative", "[OnHand] >= 0");
            });
            e.HasKey(x => new { x.ProductId, x.WarehouseId, x.LocationId });
            e.Property(x => x.OnHand).HasPrecision(18, 4);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<ProductCost>(e =>
        {
            e.ToTable("ProductCost");
            e.HasKey(x => new { x.ProductId, x.WarehouseId });
            e.Property(x => x.AvgCost).HasPrecision(18, 8);
            e.Property(x => x.QtyOnHand).HasPrecision(18, 4);
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // Branch scope filter: StockBalance/StockMovement/ProductCost carry WarehouseId directly.
        // Inventory has no Warehouse entity; branch resolution is transitive. For A1-5 the
        // global filter uses a no-op-safe predicate that does not require Warehouse navigation.
        // NOTE: branch isolation for inventory is enforced via WarehouseId linkage at the
        // application layer in Increment 2+. Schema keeps WarehouseId as plain Guid (DM-4).

        // A1-7: Transfer
        modelBuilder.Entity<Transfer>(e =>
        {
            e.ToTable("Transfer", t =>
            {
                t.HasCheckConstraint("CK_Transfer_Qty_Positive", "[Qty] > 0");
                t.HasCheckConstraint("CK_Transfer_ShippedQty_NonNegative", "[ShippedQty] >= 0");
                t.HasCheckConstraint("CK_Transfer_ReceivedQty_NonNegative", "[ReceivedQty] >= 0");
            });
            e.HasKey(x => x.TransferId);
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.Qty).HasPrecision(18, 4);
            e.Property(x => x.ShippedQty).HasPrecision(18, 4);
            e.Property(x => x.ReceivedQty).HasPrecision(18, 4);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.SourceWarehouseId, x.Status });
            e.HasIndex(x => new { x.DestWarehouseId, x.Status });
        });

        // A1-7: Adjustment
        modelBuilder.Entity<Adjustment>(e =>
        {
            e.ToTable("Adjustment");
            e.HasKey(x => x.AdjustmentId);
            e.Property(x => x.ReasonCode).HasMaxLength(50).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.SourceDocumentType).HasMaxLength(50);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.SourceDocumentType, x.SourceDocumentId });
            e.HasIndex(x => x.Status);
        });

        // A1-7: AdjustmentLine
        modelBuilder.Entity<AdjustmentLine>(e =>
        {
            e.ToTable("AdjustmentLine");
            e.HasKey(x => x.AdjustmentLineId);
            e.Property(x => x.Qty).HasPrecision(18, 4);
            e.Property(x => x.MovementType).HasMaxLength(30).IsRequired();
            e.HasOne<Adjustment>().WithMany().HasForeignKey(x => x.AdjustmentId).OnDelete(DeleteBehavior.Restrict);
        });

        // A1-7: OpeningImport
        modelBuilder.Entity<OpeningImport>(e =>
        {
            e.ToTable("OpeningImport");
            e.HasKey(x => x.OpeningImportId);
            e.Property(x => x.Status).HasMaxLength(30).IsRequired();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        // A1-7: OpeningImportLine
        modelBuilder.Entity<OpeningImportLine>(e =>
        {
            e.ToTable("OpeningImportLine", t =>
            {
                t.HasCheckConstraint("CK_OpeningImportLine_Qty_Positive", "[Qty] > 0");
            });
            e.HasKey(x => x.OpeningImportLineId);
            e.Property(x => x.Qty).HasPrecision(18, 4);
            e.Property(x => x.UnitCost).HasPrecision(18, 8);
            e.HasOne<OpeningImport>().WithMany().HasForeignKey(x => x.OpeningImportId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}