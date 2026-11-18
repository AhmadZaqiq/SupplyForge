using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupplyForge.Integrations.Infrastructure.Data;

/// <summary>
/// Design-time factory for EF migrations (dotnet ef migrations add).
/// </summary>
public sealed class IntegrationsDbContextFactory : IDesignTimeDbContextFactory<IntegrationsDbContext>
{
    public IntegrationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", IntegrationsDbContext.Schema))
            .Options);
}
