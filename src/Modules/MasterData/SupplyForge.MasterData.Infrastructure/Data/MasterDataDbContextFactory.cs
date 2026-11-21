using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupplyForge.MasterData.Infrastructure.Data;

/// <summary>
/// Design-time factory for EF migrations (dotnet ef migrations add). It never
/// opens a connection — <c>migrations add</c> only snapshots the model — and
/// <c>database update</c> is an owner-approved action run against ephemeral or
/// throwaway databases only.
/// </summary>
public sealed class MasterDataDbContextFactory : IDesignTimeDbContextFactory<MasterDataDbContext>
{
    public MasterDataDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True",
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema))
            .Options);
}
