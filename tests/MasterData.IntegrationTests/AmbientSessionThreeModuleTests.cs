using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SupplyForge.Host.Data;
using SupplyForge.Integrations.Domain;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.Integrations.Infrastructure.Outbox;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.MasterData.IntegrationTests;

/// <summary>
/// A1-1 generalises the ADR-003 ambient session from two module contexts to N
/// (MOD-TXN-01/04/05). These are the three-context analogs of
/// <c>AmbientTransactionTests</c>, run against a throwaway database holding all
/// three modules' schemas so the development database stays untouched: one
/// shared connection, one transaction, commit persists every module's row,
/// rollback loses every module's row, and the contexts are usable afterwards.
/// </summary>
public class AmbientSessionThreeModuleTests
{
    [Fact]
    public async Task MOD_TXN_01_Session_JoinsAllThreeModuleContextsOnOneConnection_ThenRestoresThem()
    {
        var dbName = ThrowawayDatabase.NewName("a1session");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var pc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(dbName));
            await using var intDb = new IntegrationsDbContext(ThrowawayDatabase.Integrations(dbName));
            await using var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await MigrateAllAsync(pc, intDb, md);

            var factory = new AmbientDbSessionFactory(pc, intDb, md);
            await using var session = await factory.BeginAsync();

            Assert.False(session.IsCompleted);

            // MOD-TXN-01/04: all three module contexts run on ONE physical
            // connection with ONE transaction — not three connections.
            Assert.Same(pc.Database.GetDbConnection(), intDb.Database.GetDbConnection());
            Assert.Same(pc.Database.GetDbConnection(), md.Database.GetDbConnection());

            await session.RollbackAsync();
            Assert.True(session.IsCompleted);

            // Cleanup restored each context to its own connection (fresh
            // connection from the captured connection string — the A0-6 spike
            // proved SetDbConnection(null) cannot restore).
            Assert.NotSame(pc.Database.GetDbConnection(), intDb.Database.GetDbConnection());
            Assert.NotSame(pc.Database.GetDbConnection(), md.Database.GetDbConnection());
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MOD_TXN_04_SessionCommit_PersistsRowsFromAllThreeModuleContexts()
    {
        var dbName = ThrowawayDatabase.NewName("a1commit");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var pc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(dbName));
            await using var intDb = new IntegrationsDbContext(ThrowawayDatabase.Integrations(dbName));
            await using var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await MigrateAllAsync(pc, intDb, md);

            var tag = $"a11commit.{Guid.NewGuid():N}";
            var branchCode = NewBranchCode();
            var factory = new AmbientDbSessionFactory(pc, intDb, md);
            var outbox = new Outbox(intDb, TimeProvider.System);

            await using (var session = await factory.BeginAsync())
            {
                pc.AuditLogs.Add(Audit(tag));
                await pc.SaveChangesAsync();
                await outbox.EnqueueAsync($"{tag}.outbox", "{\"value\":1}", version: 1, correlationId: null);
                md.Branches.Add(new Branch
                {
                    Id = Guid.NewGuid(),
                    Code = branchCode,
                    Name = "A1-1 ambient session commit row",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await md.SaveChangesAsync();

                await session.CommitAsync();
            }

            // All three modules' rows survived the commit — one transaction
            // covered three schemas (MOD-TXN-04).
            await using var checkPc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(dbName));
            await using var checkInt = new IntegrationsDbContext(ThrowawayDatabase.Integrations(dbName));
            await using var checkMd = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            Assert.Equal(1, await checkPc.AuditLogs.CountAsync(x => x.Action == tag));
            Assert.Equal(1, await checkInt.OutboxMessages.CountAsync(x => x.EventType == $"{tag}.outbox"));
            Assert.Equal(1, await checkMd.Branches.CountAsync(b => b.Code == branchCode));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    [Fact]
    public async Task MOD_TXN_05_SessionRollback_LeavesNoRowInAnyModule_ContextsRemainUsable()
    {
        var dbName = ThrowawayDatabase.NewName("a1rollback");
        await ThrowawayDatabase.CreateAsync(dbName);
        try
        {
            await using var pc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(dbName));
            await using var intDb = new IntegrationsDbContext(ThrowawayDatabase.Integrations(dbName));
            await using var md = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            await MigrateAllAsync(pc, intDb, md);

            var tag = $"a11rollback.{Guid.NewGuid():N}";
            var branchCode = NewBranchCode();
            var factory = new AmbientDbSessionFactory(pc, intDb, md);
            var outbox = new Outbox(intDb, TimeProvider.System);

            await using (var session = await factory.BeginAsync())
            {
                pc.AuditLogs.Add(Audit(tag));
                await pc.SaveChangesAsync();
                await outbox.EnqueueAsync($"{tag}.outbox", "{\"value\":1}", version: 1, correlationId: null);
                md.Branches.Add(new Branch
                {
                    Id = Guid.NewGuid(),
                    Code = branchCode,
                    Name = "A1-1 ambient session rollback row",
                    CreatedAt = DateTimeOffset.UtcNow,
                });
                await md.SaveChangesAsync();

                await session.RollbackAsync();
            }

            await using var checkPc = new PlatformCoreDbContext(ThrowawayDatabase.PlatformCore(dbName));
            await using var checkInt = new IntegrationsDbContext(ThrowawayDatabase.Integrations(dbName));
            await using var checkMd = new MasterDataDbContext(ThrowawayDatabase.MasterData(dbName), SystemBranchScope.Create());
            Assert.Equal(0, await checkPc.AuditLogs.CountAsync(x => x.Action == tag));
            Assert.Equal(0, await checkInt.OutboxMessages.CountAsync(x => x.EventType == $"{tag}.outbox"));
            Assert.Equal(0, await checkMd.Branches.CountAsync(b => b.Code == branchCode));

            // Restore proof: the SAME contexts keep working after the session,
            // in plain autocommit with no ambient transaction — the A0-6
            // spike's fresh-connection restore, now across three modules.
            pc.AuditLogs.Add(Audit($"{tag}.post"));
            await pc.SaveChangesAsync();
            // A0-8 item 5: IOutbox refuses to write outside a transaction
            // (OUT-01) — the post-session enqueue joins a fresh local one,
            // still proving the restored context works.
            await using (var postTx = await intDb.Database.BeginTransactionAsync())
            {
                await outbox.EnqueueAsync($"{tag}.post", "{\"value\":2}", version: 1, correlationId: null);
                await postTx.CommitAsync();
            }
            md.Branches.Add(new Branch
            {
                Id = Guid.NewGuid(),
                Code = NewBranchCode(),
                Name = "A1-1 post-session row",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await md.SaveChangesAsync();

            Assert.Equal(1, await checkPc.AuditLogs.CountAsync(x => x.Action == $"{tag}.post"));
            Assert.Equal(1, await checkInt.OutboxMessages.CountAsync(x => x.EventType == $"{tag}.post"));
            Assert.Equal(1, await checkMd.Branches.CountAsync(b => b.Name == "A1-1 post-session row"));
        }
        finally
        {
            await ThrowawayDatabase.DropAsync(dbName);
        }
    }

    private static async Task MigrateAllAsync(
        PlatformCoreDbContext pc,
        IntegrationsDbContext intDb,
        MasterDataDbContext md)
    {
        await pc.GetService<IMigrator>().MigrateAsync();
        await intDb.GetService<IMigrator>().MigrateAsync();
        await md.GetService<IMigrator>().MigrateAsync();
    }

    /// <summary>Branch.Code is nvarchar(20) — keep generated codes inside it.</summary>
    private static string NewBranchCode() => Guid.NewGuid().ToString("N")[..20];

    private static AuditLog Audit(string action) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = DateTimeOffset.UtcNow,
        ActorType = "a11-test",
        Action = action,
        EntityType = "A11",
        DetailsJson = null,
    };
}
