using Microsoft.EntityFrameworkCore;
using SupplyForge.Host.Data;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.PlatformCore.Application.Email;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Application.Notifications;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Services;
using SupplyForge.Shared.Notifications;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// ADR-008 delivery state on real SQL Server (NFR-050), constructed through
/// the same classes the Host DI registers: PLT-051 visible pending when
/// email is off, PLT-052 failure recording with INT-003 backoff and the
/// Failed dead-letter, and the TD-2 manual mark-sent contract. The HTTP
/// legs (failure-injected host, mark-sent endpoint) live in
/// <c>NotificationDeliveryHttpTests</c>.
/// </summary>
public class NotificationDeliveryStateTests
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

    [Fact]
    public async Task PLT_051_EmailDisabled_RaiseStoresVisiblePending_WithoutAdapterAttempt()
    {
        var options = new EmailOptions { Mode = EmailMode.Disabled };
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var store = new PlatformSharedServices(pc, TimeProvider.System);
        var email = new FakeEmailSender(options);
        var delivery = new NotificationDeliveryService(
            store, email, options, store, TimeProvider.System);
        var raise = new NotificationRaiseService(
            new AmbientDbSessionFactory(pc, intDb), store, delivery);

        var id = await raise.RaiseAsync(
            "a0.pending", "Pending only", "email off", link: null);

        // PLT-051: the row is stored, readable, and honestly pending — the
        // adapter was never touched and no retry was ever scheduled, so the
        // worker sweep can never pick it up.
        var row = await pc.Notifications.FirstAsync(n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.Equal(0, row.DeliveryAttempts);
        Assert.Null(row.SentAt);
        Assert.Null(row.NextAttemptAt);
        Assert.Empty(email.Sent);

        // Visible pending through the read-API source (ADR-008 read leg).
        var visible = Assert.Single(
            await store.ListVisibleToUserAsync(Guid.NewGuid(), 50),
            n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Pending, visible.DeliveryStatus);

        // Our row must never be sweepable — other tests may have their own
        // due rows on this shared database, so assert on this id only.
        Assert.DoesNotContain(
            await store.ListDueForDeliveryAsync(10), d => d.Id == id);

        await pc.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task PLT_052_FailureRecordsPendingWithBackoff_ThenDeadLettersAtMaxAttempts()
    {
        var options = new EmailOptions
        {
            Mode = EmailMode.Fake,
            SimulateFailures = true,
            MaxAttempts = 2,
            // Far-future first retry: the "not due yet" assertions below are
            // deterministic regardless of test speed.
            RetryBaseDelay = TimeSpan.FromMinutes(1),
            RetryMaxDelay = TimeSpan.FromMinutes(5),
        };
        await using var pc = CreatePlatformDb();
        await using var intDb = CreateIntegrationsDb();
        var store = new PlatformSharedServices(pc, TimeProvider.System);
        var email = new FakeEmailSender(options);
        var delivery = new NotificationDeliveryService(
            store, email, options, store, TimeProvider.System);
        var raise = new NotificationRaiseService(
            new AmbientDbSessionFactory(pc, intDb), store, delivery);

        var id = await raise.RaiseAsync(
            "a0.delivery", "Delivery state", "fails on purpose", link: null);

        // PLT-052: the adapter failure is recorded state, not an exception —
        // the raise returned the id (the endpoint answers 201, never 500).
        // AsNoTracking throughout these verifications: the store writes
        // with conditional UPDATEs, so the assertions must read database
        // state, not the tracked insert snapshot.
        var row = await pc.Notifications.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.Equal(1, row.DeliveryAttempts);
        Assert.NotNull(row.NextAttemptAt);
        Assert.True(
            row.NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(30),
            "the first retry must be scheduled with backoff, not immediately.");
        Assert.Contains("Simulated delivery failure", row.LastDeliveryError!);
        Assert.Empty(email.Sent);

        // Not due yet → the sweep must not see the row (no double-send).
        // Our row must never be sweepable — other tests may have their own
        // due rows on this shared database, so assert on this id only.
        Assert.DoesNotContain(
            await store.ListDueForDeliveryAsync(10), d => d.Id == id);

        // Force the retry due (bulk UPDATE: the verification read above was
        // intentionally non-tracking, so mutate the row through the store
        // path instead of a detached instance): attempt 2 == MaxAttempts →
        // Failed dead-letter (INT-003), recorded through the same delivery
        // path the worker uses.
        await pc.Notifications
            .Where(n => n.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(
                n => n.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));

        var due = Assert.Single(
            await store.ListDueForDeliveryAsync(10), d => d.Id == id);
        Assert.Equal(id, due.Id);
        Assert.Equal(1, due.DeliveryAttempts);
        await delivery.TryDeliverAsync(
            due.Id, due.NotificationType, due.Title, due.Body, due.DeliveryAttempts);

        var failed = await pc.Notifications.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Failed, failed.DeliveryStatus);
        Assert.Equal(2, failed.DeliveryAttempts);
        Assert.Null(failed.NextAttemptAt);
        Assert.Null(failed.SentAt);
        Assert.Contains("Simulated delivery failure", failed.LastDeliveryError!);

        // PLT-052 read surface: the dead-letter is visible to any user.
        var visible = Assert.Single(
            await store.ListVisibleToUserAsync(Guid.NewGuid(), 50),
            n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Failed, visible.DeliveryStatus);
        Assert.Equal(2, visible.DeliveryAttempts);
        Assert.Contains("Simulated delivery failure", visible.LastDeliveryError!);

        await pc.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TD_2_MarkSent_RecoversPendingAndFailedRows_AlreadySentIdempotent_NotFound()
    {
        await using var pc = CreatePlatformDb();
        var store = new PlatformSharedServices(pc, TimeProvider.System);
        var delivery = new NotificationDeliveryService(
            store, new FakeEmailSender(new EmailOptions()), new EmailOptions(),
            store, TimeProvider.System);

        // A visible-pending row (email off) and a dead-lettered row.
        var pendingId = await store.RaiseAsync(
            "a0.td2", userId: null, branchId: null, "Pending", "never sent", link: null);
        var failedId = await store.RaiseAsync(
            "a0.td2", userId: null, branchId: null, "Failed", "gave up", link: null);
        var failedRow = await pc.Notifications.FirstAsync(n => n.Id == failedId);
        failedRow.DeliveryStatus = NotificationDeliveryStatus.Failed;
        failedRow.LastDeliveryError = "boom";
        failedRow.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(5);
        await pc.SaveChangesAsync();

        // Pending → Sent through the service: transition + audit row (TD-2).
        Assert.Equal(
            NotificationMarkSentResult.MarkedSent,
            await delivery.MarkSentAsync(pendingId, "td2-tester"));
        var pendingAfter = await pc.Notifications.AsNoTracking().FirstAsync(n => n.Id == pendingId);
        Assert.Equal(NotificationDeliveryStatus.Sent, pendingAfter.DeliveryStatus);
        Assert.NotNull(pendingAfter.SentAt);
        Assert.Null(pendingAfter.NextAttemptAt);
        Assert.Equal(
            1,
            await pc.AuditLogs.CountAsync(a =>
                a.Action == "notification.mark_sent" && a.EntityId == pendingId.ToString()));

        // Failed dead-letter → Sent; the stale diagnosis is cleared and no
        // adapter attempt is invented (attempts stay as they were).
        Assert.Equal(
            NotificationMarkSentResult.MarkedSent,
            await store.MarkSentManuallyAsync(failedId));
        var failedAfter = await pc.Notifications.AsNoTracking().FirstAsync(n => n.Id == failedId);
        Assert.Equal(NotificationDeliveryStatus.Sent, failedAfter.DeliveryStatus);
        Assert.NotNull(failedAfter.SentAt);
        Assert.Null(failedAfter.LastDeliveryError);
        Assert.Null(failedAfter.NextAttemptAt);
        Assert.Equal(0, failedAfter.DeliveryAttempts);

        // Idempotent repeat + unknown id (the endpoint maps these to 200/404).
        Assert.Equal(
            NotificationMarkSentResult.AlreadySent,
            await store.MarkSentManuallyAsync(failedId));
        Assert.Equal(
            NotificationMarkSentResult.NotFound,
            await store.MarkSentManuallyAsync(Guid.NewGuid()));

        await pc.Notifications
            .Where(n => n.Id == pendingId || n.Id == failedId)
            .ExecuteDeleteAsync();
        await pc.AuditLogs
            .Where(a => a.EntityId == pendingId.ToString() || a.EntityId == failedId.ToString())
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task PLT_052_ConcurrentDeliveryAttempts_ExactlyOneClaimWins_SingleAdapterCall()
    {
        // Duplicate-processing guard (PLT-052): two processors race the same
        // row with the same read count — the CAS claim admits exactly one,
        // the loser must not touch the adapter, and the row ends Sent with
        // one recorded attempt. Two contexts = two real connections, the
        // same situation as two Host processes sweeping one database.
        await using var pc1 = CreatePlatformDb();
        await using var pc2 = CreatePlatformDb();
        var store1 = new PlatformSharedServices(pc1, TimeProvider.System);
        var store2 = new PlatformSharedServices(pc2, TimeProvider.System);
        var sender = new CountingEmailSender();
        var options = new EmailOptions
        {
            Mode = EmailMode.Fake,
            RetryBaseDelay = TimeSpan.FromMinutes(1),
        };
        var delivery1 = new NotificationDeliveryService(
            store1, sender, options, store1, TimeProvider.System);
        var delivery2 = new NotificationDeliveryService(
            store2, sender, options, store2, TimeProvider.System);

        var id = await store1.RaiseAsync(
            "a0.race", userId: null, branchId: null, "Race", "one send only", link: null);

        await Task.WhenAll(
            delivery1.TryDeliverAsync(id, "a0.race", "Race", "one send only", attemptsSoFar: 0),
            delivery2.TryDeliverAsync(id, "a0.race", "Race", "one send only", attemptsSoFar: 0));

        Assert.Equal(1, sender.Calls);

        var row = await pc1.Notifications.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Sent, row.DeliveryStatus);
        Assert.Equal(1, row.DeliveryAttempts);
        Assert.NotNull(row.SentAt);

        await pc1.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task INT_004_SendTimeout_RecordsFailure_InsteadOfHangingTheAttempt()
    {
        var options = new EmailOptions
        {
            Mode = EmailMode.Fake,
            SendTimeout = TimeSpan.FromMilliseconds(150),
            // Far-future retry: the row stays visible pending for the test.
            RetryBaseDelay = TimeSpan.FromMinutes(1),
            RetryMaxDelay = TimeSpan.FromMinutes(5),
        };
        await using var pc = CreatePlatformDb();
        var store = new PlatformSharedServices(pc, TimeProvider.System);
        var delivery = new NotificationDeliveryService(
            store, new HangingEmailSender(), options, store, TimeProvider.System);

        var id = await store.RaiseAsync(
            "a0.timeout", userId: null, branchId: null, "Slow", "hangs forever", link: null);

        var started = DateTime.UtcNow;
        await delivery.TryDeliverAsync(
            id, "a0.timeout", "Slow", "hangs forever", attemptsSoFar: 0);
        var elapsed = DateTime.UtcNow - started;

        Assert.True(
            elapsed < TimeSpan.FromSeconds(10),
            $"a hung adapter must be bounded by SendTimeout, but the attempt took {elapsed}.");
        var row = await pc.Notifications.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.Equal(1, row.DeliveryAttempts);
        Assert.Contains("timed out", row.LastDeliveryError!);
        Assert.NotNull(row.NextAttemptAt);

        await pc.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task INT_004_HostShutdownCancellation_PropagatesWithoutRecordingAnOutcome()
    {
        var options = new EmailOptions { Mode = EmailMode.Fake };
        await using var pc = CreatePlatformDb();
        var store = new PlatformSharedServices(pc, TimeProvider.System);
        var delivery = new NotificationDeliveryService(
            store, new FakeEmailSender(options), options, store, TimeProvider.System);

        var id = await store.RaiseAsync(
            "a0.shutdown", userId: null, branchId: null, "Shutdown", "stop the host", link: null);

        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delivery.TryDeliverAsync(
            id, "a0.shutdown", "Shutdown", "stop the host", attemptsSoFar: 0, shutdown.Token));

        // Shutdown is not a delivery outcome: nothing recorded as failed,
        // nothing marked Sent. The claim itself may have executed before the
        // cancellation landed (the attempt stays counted — at-least-once
        // across shutdowns), so only the outcome columns are asserted.
        var row = await pc.Notifications.AsNoTracking().FirstAsync(n => n.Id == id);
        Assert.Equal(NotificationDeliveryStatus.Pending, row.DeliveryStatus);
        Assert.Null(row.LastDeliveryError);
        Assert.Null(row.SentAt);
        Assert.True(row.DeliveryAttempts <= 1);

        await pc.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Adapter that records how often it was actually called.</summary>
    private sealed class CountingEmailSender : IEmailSender
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task SendAsync(
            string to, string subject, string body, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.CompletedTask;
        }
    }

    /// <summary>Adapter that hangs until its token is cancelled (INT-004).</summary>
    private sealed class HangingEmailSender : IEmailSender
    {
        public async Task SendAsync(
            string to, string subject, string body, CancellationToken cancellationToken = default) =>
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
    }
}
