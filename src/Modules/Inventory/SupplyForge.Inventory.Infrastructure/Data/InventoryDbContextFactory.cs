using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupplyForge.Inventory.Infrastructure.Data;

public sealed class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .Options, BranchScopeDefaults.NoScope);
}