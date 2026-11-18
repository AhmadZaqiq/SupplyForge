using Microsoft.EntityFrameworkCore;
using SupplyForge.Host.Data;
using SupplyForge.Integrations.Domain;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.Integrations.Infrastructure.Outbox;
using SupplyForge.PlatformCore.Application.Email;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Application.Notifications;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Services;
using SupplyForge.Shared.Data;
using SupplyForge.Shared.Idempotency;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// ADR-003 "How tests prove it" rows that are feasible in A0 (NFR-050, real
/// SQL Server). Production parity: these tests construct the same
/// <see cref="AmbientDbSessionFactory"/> class the Host DI registers —
/// Option B (one shared connection + transaction across the PlatformCore and
/// Integrations contexts) against the real database, never the in-memory
/// provider. The ADR's GR/SO multi-module rollback row lands in A2/A3 and
/// TD-10 in the shipment slice (recorded in the A0-6 plan); the two-context
/// rollback test here is the A0 analog. The HTTP round-trip through the real
/// Host DI composition lives in <c>HttpAuthMatrixTests</c> (PLT-050).
/// </summary>
public class AmbientTransactionTests
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    private static PlatformCoreDbContext CreatePlatformDb()
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;
        return new PlatformCoreDbContext(opts);
    }

    private static IntegrationsDbContext CreateIntegrationsDb()
    {
        var opts = new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IntegrationsDbContext.Schema))
            .Options;
        return new IntegrationsDbContext(opts);
    }

    private static AuditLog Audit(string action) => new()
    {
        Id = Guid.NewGuid(),
        OccurredAt = DateTimeOffset.UtcNow,
        ActorType = "a06-test",
        Action = action,
        EntityType = "A06",
        DetailsJson = null,
    };

    [Fact]
    public async Task MOD_TXN_01_Session_JoinsBothModuleContextsOnOneConnection_ThenRestoresThem()
    {
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var factory = new AmbientDbSessionFactory(pc, intDb);

        await using var session = await factory.BeginAsync();

        Assert.False(session.IsCompleted);
        // MOD-TXN-01/04: both module contexts run on ONE physical
        // connection with ONE transaction — not two connections.
        Assert.Same(pc.Database.GetDbConnection(), intDb.Database.GetDbConnection());

        await session.RollbackAsync();
        Assert.True(session.IsCompleted);

        // Cleanup restored each context to its own connection (fresh
        // connection from the captured connection string — the A0-6 spike
        // proved SetDbConnection(null) cannot restore).
        Assert.NotSame(pc.Database.GetDbConnection(), intDb.Database.GetDbConnection());
    }

    [Fact]
    public async Task OUT_01_ForcedDomainFailureAfterOutboxInsert_RollsBackDomainAndOutboxRows()
    {
        var tag = $"a06atomic.{Guid.NewGuid():N}";
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var factory = new AmbientDbSessionFactory(pc, intDb);
        var outbox = new Outbox(intDb, TimeProvider.System);

        var forcedFailure = false;
        try
        {
            // Dispose-without-commit: unwinding the await using must roll
            // the whole session back — no orphan outbox row, no partial
            // domain state (ADR-003 outbox-atomicity row, OUT-01).
            await using (var session = await factory.BeginAsync())
            {
                pc.AuditLogs.Add(Audit($"{tag}.domain"));
                await pc.SaveChangesAsync();
                await outbox.EnqueueAsync($"{tag}.outbox", "{\"value\":1}", version: 1);

                // Forced domain failure AFTER the outbox insert attempt.
                throw new InvalidOperationException("forced domain failure (A0-6)");
            }
        }
        catch (InvalidOperationException ex) when (ex.Message == "forced domain failure (A0-6)")
        {
            forcedFailure = true;
        }

        Assert.True(forcedFailure, "the forced domain failure must surface to the caller");

        await using var checkPc = CreatePlatformDb();
        await using var checkInt = CreateIntegrationsDb();
        Assert.Equal(0, await checkPc.AuditLogs.CountAsync(x => x.Action!.StartsWith(tag)));
        Assert.Equal(0, await checkInt.OutboxMessages.CountAsync(x => x.EventType!.StartsWith(tag)));
    }

    [Fact]
    public async Task MOD_TXN_04_SessionCommit_PersistsRowsFromBothModuleContexts()
    {
        var tag = $"a06commit.{Guid.NewGuid():N}";
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var factory = new AmbientDbSessionFactory(pc, intDb);
        var outbox = new Outbox(intDb, TimeProvider.System);

        await using (var session = await factory.BeginAsync())
        {
            pc.AuditLogs.Add(Audit($"{tag}.domain"));
            await pc.SaveChangesAsync();
            await outbox.EnqueueAsync($"{tag}.outbox", "{\"value\":1}", version: 1);
            await session.CommitAsync();
        }

        // Both modules' rows survive the commit — one transaction covered
        // both schemas (MOD-TXN-04).
        await using var checkPc = CreatePlatformDb();
        await using var checkInt = CreateIntegrationsDb();
        Assert.Equal(1, await checkPc.AuditLogs.CountAsync(x => x.Action!.StartsWith(tag)));
        Assert.Equal(1, await checkInt.OutboxMessages.CountAsync(x => x.EventType!.StartsWith(tag)));

        await checkPc.AuditLogs.Where(x => x.Action!.StartsWith(tag)).ExecuteDeleteAsync();
        await checkInt.OutboxMessages.Where(x => x.EventType!.StartsWith(tag)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task MOD_TXN_05_SessionRollback_LeavesNoRowInEitherModule_ContextsRemainUsable()
    {
        var tag = $"a06rollback.{Guid.NewGuid():N}";
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var factory = new AmbientDbSessionFactory(pc, intDb);
        var outbox = new Outbox(intDb, TimeProvider.System);

        await using (var session = await factory.BeginAsync())
        {
            pc.AuditLogs.Add(Audit($"{tag}.rolledback"));
            await pc.SaveChangesAsync();
            await outbox.EnqueueAsync($"{tag}.rolledback", "{\"value\":1}", version: 1);
            await session.RollbackAsync();
        }

        await using var checkPc = CreatePlatformDb();
        await using var checkInt = CreateIntegrationsDb();
        Assert.Equal(0, await checkPc.AuditLogs.CountAsync(x => x.Action!.StartsWith(tag)));
        Assert.Equal(0, await checkInt.OutboxMessages.CountAsync(x => x.EventType!.StartsWith(tag)));

        // Restore proof: the SAME contexts keep working after the session,
        // in plain autocommit with no ambient transaction — the A0-6 spike's
        // fresh-connection restore, now asserted in the product path.
        pc.AuditLogs.Add(Audit($"{tag}.post"));
        await pc.SaveChangesAsync();
        await outbox.EnqueueAsync($"{tag}.post", "{\"value\":2}", version: 1);

        Assert.Equal(1, await checkPc.AuditLogs.CountAsync(x => x.Action == $"{tag}.post"));
        Assert.Equal(1, await checkInt.OutboxMessages.CountAsync(x => x.EventType == $"{tag}.post"));

        await checkPc.AuditLogs.Where(x => x.Action!.StartsWith(tag)).ExecuteDeleteAsync();
        await checkInt.OutboxMessages.Where(x => x.EventType!.StartsWith(tag)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task MOD_TXN_02_NotificationRaise_GuardEmail_FiresOnlyAfterSessionCompletion()
    {
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var capturing = new CapturingSessionFactory(new AmbientDbSessionFactory(pc, intDb));
        var guard = new OpenTransactionGuardEmail(
            () => capturing.LastSession is { IsCompleted: true });
        var store = new PlatformSharedServices(pc, TimeProvider.System);
        var service = new NotificationRaiseService(capturing, store, guard);

        var id = await service.RaiseAsync(
            "a06.raise", "A0-6 raise", "email after commit", link: null);

        // ADR-003 "no external call in TXN": exactly one send, observed only
        // with the session already completed — the guard throws if the
        // adapter is ever invoked before that (MOD-TXN-02).
        var observed = Assert.Single(guard.SessionCompletedAtSend);
        Assert.True(observed, "the email must fire after the session completed");
        Assert.NotNull(capturing.LastSession);

        // The store write really committed (not rolled back): the row is
        // visible to a fresh context.
        await using var check = CreatePlatformDb();
        var row = await check.Notifications.FirstAsync(n => n.Id == id);
        Assert.Equal("A0-6 raise", row.Title);
        await check.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task IDEM_05_ConcurrentSameKeyClaims_ExactlyOneClaimed_SingleRow()
    {
        var key = $"a06-idem-{Guid.NewGuid():N}";
        await using var db1 = CreatePlatformDb();
        await using var db2 = CreatePlatformDb();
        var store1 = new PlatformSharedServices(db1, TimeProvider.System);
        var store2 = new PlatformSharedServices(db2, TimeProvider.System);

        // IDEM-05: two real connections race the same key — exactly one
        // claim may win; the loser must receive an accurate status instead
        // of an exception (no double post), and the table must end up with a
        // single row for the key.
        var results = await Task.WhenAll(
            store1.TryClaimAsync(key, "hash-a"),
            store2.TryClaimAsync(key, "hash-a"));

        Assert.Equal(1, results.Count(r => r.Status == IdempotencyClaimStatus.Claimed));
        Assert.All(
            results.Where(r => r.Status != IdempotencyClaimStatus.Claimed),
            r => Assert.True(
                r.Status is IdempotencyClaimStatus.InProgress
                    or IdempotencyClaimStatus.AlreadyCompleted,
                $"the losing claim must get an accurate status, got {r.Status}"));

        await using var check = CreatePlatformDb();
        Assert.Equal(1, await check.IdempotencyKeys.CountAsync(x => x.Key == key));
        await check.IdempotencyKeys.Where(x => x.Key == key).ExecuteDeleteAsync();
    }

    /// <summary>
    /// Remembers the session the production factory produced so the email
    /// guard can observe its state (test double over the real factory).
    /// </summary>
    private sealed class CapturingSessionFactory : IDbSessionFactory
    {
        private readonly IDbSessionFactory _inner;

        public CapturingSessionFactory(IDbSessionFactory inner) => _inner = inner;

        public IDbSession? LastSession { get; private set; }

        public async Task<IDbSession> BeginAsync(CancellationToken cancellationToken = default)
        {
            LastSession = await _inner.BeginAsync(cancellationToken);
            return LastSession;
        }
    }

    /// <summary>
    /// ADR-003 "no external call in TXN": throws if invoked while the
    /// ambient session has not completed; records what it observed.
    /// </summary>
    private sealed class OpenTransactionGuardEmail : IEmailSender
    {
        private readonly Func<bool> _sessionCompleted;

        public OpenTransactionGuardEmail(Func<bool> sessionCompleted) =>
            _sessionCompleted = sessionCompleted;

        public List<bool> SessionCompletedAtSend { get; } = new();

        public Task SendAsync(
            string to,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            var completed = _sessionCompleted();
            SessionCompletedAtSend.Add(completed);
            if (!completed)
            {
                throw new InvalidOperationException(
                    "MOD-TXN-02 violation: email invoked before the ambient session completed.");
            }

            return Task.CompletedTask;
        }
    }
}
