using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// Boundary check for the approved A0 delivery-state migration
/// (20261119033923_AddNotificationDeliveryState): it must backfill rows that
/// predate delivery tracking to visible Pending without losing any existing
/// data, and must create the sweep index. Runs against a throwaway database
/// created for the test (never the shared dev database): apply only the
/// initial migration, insert a pre-migration-shaped row, apply the full
/// migration history, then assert the result — exactly the sequence the
/// boundary requires verifying before the migration is trusted anywhere.
/// </summary>
public class NotificationDeliveryMigrationTests
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    /// <summary>Last migration before delivery state existed.</summary>
    private const string InitialMigrationId = "20261116221419_InitialPlatformCore";

    [Fact]
    public async Task MIGRATION_AddNotificationDeliveryState_BackfillsExistingRowToPending_NoDataLoss()
    {
        var dbName = $"SupplyForge_MigTest_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(dbName);
        try
        {
            var options = new DbContextOptionsBuilder<PlatformCoreDbContext>()
                .UseSqlServer(DatabaseFor(dbName), sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
                .Options;

            await using var db = new PlatformCoreDbContext(options);

            // 1) Schema as it existed before delivery state.
            db.GetService<IMigrator>().Migrate(InitialMigrationId);

            // 2) A row written in the pre-migration shape (only the original
            //    nine columns exist at this point).
            var id = Guid.NewGuid();
            var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
            await db.Database.ExecuteSqlRawAsync(
                @"INSERT INTO platform.Notification
                      (Id, NotificationType, UserId, BranchId, Title, Body, Link, CreatedAt, ReadAt)
                  VALUES ({0}, 'system', NULL, NULL, {1}, {2}, NULL, {3}, NULL)",
                id,
                "Pre-migration",
                "row written before delivery state",
                createdAt);

            // 3) Apply the rest of the history (the delivery-state migration).
            db.GetService<IMigrator>().Migrate();

            // 4) Backfill + no data loss: the row survives untouched apart
            //    from the new columns, which default to honest visible
            //    pending — never auto-swept (NextAttemptAt NULL) and
            //    closable via TD-2 manual mark-sent.
            var row = await db.Notifications.SingleAsync(n => n.Id == id);
            Assert.Equal("Pre-migration", row.Title);
            Assert.Equal("row written before delivery state", row.Body);
            Assert.Equal(createdAt, row.CreatedAt);
            Assert.Null(row.UserId);
            Assert.Null(row.ReadAt);

            Assert.Equal("Pending", row.DeliveryStatus);
            Assert.Equal(0, row.DeliveryAttempts);
            Assert.Null(row.NextAttemptAt);
            Assert.Null(row.SentAt);
            Assert.Null(row.LastDeliveryError);

            Assert.Equal(1, await db.Notifications.CountAsync());

            // The sweep index exists (delivery-status + due-time predicate).
            var indexCount = db.Database.SqlQueryRaw<int>(
                @"SELECT COUNT(*) AS Value FROM sys.indexes
                  WHERE object_id = OBJECT_ID('platform.Notification')
                    AND name = 'IX_Notification_DeliveryStatus_NextAttemptAt'");
            Assert.Equal(1, await indexCount.SingleAsync());
        }
        finally
        {
            await DropDatabaseAsync(dbName);
        }
    }

    private static string DatabaseFor(string dbName) =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = dbName }.ConnectionString;

    private static string MasterConnectionString() =>
        new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;

    private static async Task CreateDatabaseAsync(string dbName)
    {
        await using var master = new SqlConnection(MasterConnectionString());
        await master.OpenAsync();
        await using var cmd = master.CreateCommand();
        // dbName is generated from a GUID — no user input reaches the SQL.
        cmd.CommandText = $"CREATE DATABASE [{dbName}]";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropDatabaseAsync(string dbName)
    {
        // The database exists only for this test: single-user evicts any
        // pooled connections before the drop.
        await using var master = new SqlConnection(MasterConnectionString());
        await master.OpenAsync();
        await using var cmd = master.CreateCommand();
        cmd.CommandText =
            $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();
    }
}
