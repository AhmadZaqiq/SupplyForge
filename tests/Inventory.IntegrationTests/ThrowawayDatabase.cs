using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.Inventory.Infrastructure.Data;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.Inventory.IntegrationTests;

/// <summary>
/// Throwaway-database helpers for the A1-5 Inventory tests.
///
/// <para>
/// The A1 boundary for this slice: the shared development database is never
/// modified and no migration is ever applied to it. Every test here therefore
/// creates its own database, applies the migrations it needs to that database
/// only, and drops it afterwards — the same pattern
/// <c>NotificationDeliveryMigrationTests</c> established for A0. Nothing in
/// this class ever opens a connection to the development database.
/// </para>
/// </summary>
internal static class ThrowawayDatabase
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    /// <summary>Unique name — no user input ever reaches the SQL below.</summary>
    public static string NewName(string prefix) => $"SupplyForge_{prefix}_{Guid.NewGuid():N}";

    public static string For(string dbName) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = dbName }.ConnectionString;

    public static async Task CreateAsync(string dbName)
    {
        await using var master = new SqlConnection(MasterConnectionString());
        await master.OpenAsync();
        await using var cmd = master.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{dbName}]";
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task DropAsync(string dbName)
    {
        // The database exists only for one test: single-user evicts any
        // pooled connections before the drop.
        await using var master = new SqlConnection(MasterConnectionString());
        await master.OpenAsync();
        await using var cmd = master.CreateCommand();
        cmd.CommandText =
            $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();
    }

    public static DbContextOptions<InventoryDbContext> Inventory(string dbName) =>
        new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlServer(For(dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", InventoryDbContext.Schema))
            .Options;

    public static DbContextOptions<MasterDataDbContext> MasterData(string dbName) =>
        new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer(For(dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", MasterDataDbContext.Schema))
            .Options;

    public static DbContextOptions<PlatformCoreDbContext> PlatformCore(string dbName) =>
        new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(For(dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;

    public static DbContextOptions<IntegrationsDbContext> Integrations(string dbName) =>
        new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseSqlServer(For(dbName), sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IntegrationsDbContext.Schema))
            .Options;

    private static string MasterConnectionString() =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
}