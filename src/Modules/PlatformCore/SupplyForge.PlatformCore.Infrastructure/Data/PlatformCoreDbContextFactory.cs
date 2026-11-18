using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupplyForge.PlatformCore.Infrastructure.Data;

/// <summary>
/// Design-time factory for EF migrations (dotnet ef migrations add).
/// </summary>
public sealed class PlatformCoreDbContextFactory : IDesignTimeDbContextFactory<PlatformCoreDbContext>
{
    public PlatformCoreDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options);
}
