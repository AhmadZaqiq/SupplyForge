using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Http;
using SupplyForge.Host.RateLimiting;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A0-2b (SEC-04): partition-limiter eviction must bound dictionary growth
/// without racing active acquires. Uses the internal test hooks on
/// <see cref="AuthRateLimiterPolicy"/> (InternalsVisibleTo).
/// </summary>
public sealed class RateLimiterEvictionTests
{
    private static AuthRateLimitOptions ShortWindowOptions() => new()
    {
        AuthWindow = TimeSpan.FromMilliseconds(200),
        AuthIpLimit = 50,
        AuthAccountLimit = 50,
    };

    private static DefaultHttpContext CreateContext(string ip, string? userName)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        if (userName is not null)
        {
            context.Items[AuthRateLimiterPolicy.LoginUserNameItemKey] = userName;
        }

        return context;
    }

    private static async Task AcquireOnceAsync(AuthRateLimiterPolicy policy, DefaultHttpContext context)
    {
        var partition = policy.GetPartition(context);
        var limiter = partition.Factory!(partition.PartitionKey);
        using var lease = await limiter.AcquireAsync(1);
        Assert.True(lease.IsAcquired);
        limiter.Dispose();
    }

    /// <summary>
    /// Concurrent path: only asserts that acquire/sweep complete without
    /// throwing (ObjectDisposedException). Lease denial is expected when a
    /// shared window is exhausted — that is the rate limiter working.
    /// </summary>
    private static async Task AcquireAllowingDenialAsync(AuthRateLimiterPolicy policy, DefaultHttpContext context)
    {
        var partition = policy.GetPartition(context);
        var limiter = partition.Factory!(partition.PartitionKey);
        using var lease = await limiter.AcquireAsync(1);
        limiter.Dispose();
    }

    [Fact]
    public async Task SEC_04_FirstAcquire_CreatesIpAndAccountLimiters()
    {
        using var policy = new AuthRateLimiterPolicy(ShortWindowOptions(), enableAutoSweep: false);

        await AcquireOnceAsync(policy, CreateContext("198.51.100.10", "alice"));

        Assert.Equal(2, policy.TrackedLimiterCount);
    }

    [Fact]
    public void SEC_04_Sweep_DoesNotDisposeEntriesWithActiveHold()
    {
        using var policy = new AuthRateLimiterPolicy(ShortWindowOptions(), enableAutoSweep: false);

        using (policy.HoldEntriesForTest("203.0.113.7", "u:bob"))
        {
            Assert.Equal(2, policy.TrackedLimiterCount);

            // Idle timestamps are in the past, but ActiveCount > 0 must win.
            policy.ForceIdleForTest();
            policy.SweepNow();
            Assert.Equal(2, policy.TrackedLimiterCount);
        }

        // Released: now idle + ActiveCount == 0 → evicted.
        policy.ForceIdleForTest();
        policy.SweepNow();
        Assert.Equal(0, policy.TrackedLimiterCount);
    }

    [Fact]
    public async Task SEC_04_Sweep_RecentlyUsedEntries_AreNotEvicted()
    {
        using var policy = new AuthRateLimiterPolicy(ShortWindowOptions(), enableAutoSweep: false);

        await AcquireOnceAsync(policy, CreateContext("198.51.100.11", "carol"));
        Assert.Equal(2, policy.TrackedLimiterCount);

        // Last-use is now; sweep must keep the entries despite ActiveCount == 0.
        policy.SweepNow();
        Assert.Equal(2, policy.TrackedLimiterCount);
    }

    [Fact]
    public async Task SEC_04_Sweep_EvictsIdleEntries_ThenRecreatesBuckets()
    {
        using var policy = new AuthRateLimiterPolicy(ShortWindowOptions(), enableAutoSweep: false);

        var context = CreateContext("198.51.100.12", "dave");
        await AcquireOnceAsync(policy, context);
        Assert.Equal(2, policy.TrackedLimiterCount);

        policy.ForceIdleForTest();
        policy.SweepNow();
        Assert.Equal(0, policy.TrackedLimiterCount);

        // Recreation: a later request builds fresh children and still works.
        await AcquireOnceAsync(policy, context);
        Assert.Equal(2, policy.TrackedLimiterCount);
    }

    [Fact]
    public async Task SEC_04_AutoSweep_DisposesIdleEntriesWithoutManualCall()
    {
        var options = new AuthRateLimitOptions
        {
            AuthWindow = TimeSpan.FromMilliseconds(150),
            AuthIpLimit = 10,
            AuthAccountLimit = 10,
        };

        using var policy = new AuthRateLimiterPolicy(options, enableAutoSweep: true);
        await AcquireOnceAsync(policy, CreateContext("198.51.100.13", "erin"));
        Assert.Equal(2, policy.TrackedLimiterCount);

        // idle = 2×150ms = 300ms; sweep period = 150ms → evicted well within 2s.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (policy.TrackedLimiterCount > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.Equal(0, policy.TrackedLimiterCount);
    }

    [Fact]
    public async Task SEC_04_ConcurrentAcquireAndSweep_NoObjectDisposedException()
    {
        using var policy = new AuthRateLimiterPolicy(ShortWindowOptions(), enableAutoSweep: false);

        var errors = new ConcurrentQueue<Exception>();
        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 40; i++)
                {
                    // Shared IP + per-worker account: stress one IP bucket while
                    // accounts churn, with sweeps interleaved on every iteration.
                    var context = CreateContext("192.0.2.55", $"user{worker}");
                    await AcquireAllowingDenialAsync(policy, context);
                    policy.SweepNow();
                }
            }
            catch (Exception ex)
            {
                errors.Enqueue(ex);
            }
        })).ToArray();

        await Task.WhenAll(workers);
        Assert.Empty(errors);
    }
}
