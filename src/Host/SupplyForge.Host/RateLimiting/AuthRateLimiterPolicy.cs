using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace SupplyForge.Host.RateLimiting;

internal readonly record struct AuthPartitionKey(string Ip, string Account);

/// <summary>
/// SEC-04 / PLT-012 (A0-2): rate-limit policy for login/refresh. Each request
/// acquires two fixed windows — one per remote IP (NAT-aware ceiling) and one
/// per account (ADR-004: per IP + account). Registered with the built-in
/// RateLimiting middleware; counts are in-memory per instance (MOD-8: no Redis).
///
/// A0-2b: partition limiters are tracked in entries with an active-use count
/// and a last-use timestamp. A periodic sweep disposes entries that have been
/// idle for more than 2× the window while no request holds them, so arbitrary
/// user names / client IPs cannot grow the dictionaries without bound.
/// </summary>
internal sealed class AuthRateLimiterPolicy : IRateLimiterPolicy<AuthPartitionKey>, IDisposable
{
    public const string PolicyName = "auth-sensitive";
    public const string LoginUserNameItemKey = "SupplyForge.RateLimit.LoginUserName";

    /// <summary>Children idle longer than 2× the window are eviction candidates (A0-2b).</summary>
    private const int EvictionIdleWindowMultiplier = 2;

    private readonly TimeSpan _window;
    private readonly TimeSpan _idleTimeout;
    private readonly int _ipLimit;
    private readonly int _accountLimit;
    private readonly ConcurrentDictionary<string, LimiterEntry> _ipLimiters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LimiterEntry> _accountLimiters = new(StringComparer.Ordinal);
    private readonly Timer? _sweepTimer;

    public AuthRateLimiterPolicy(AuthRateLimitOptions options)
        : this(options, enableAutoSweep: true)
    {
    }

    /// <summary>Test hook: optionally suppress the background sweep so tests can call <see cref="SweepNow"/> deterministically.</summary>
    internal AuthRateLimiterPolicy(AuthRateLimitOptions options, bool enableAutoSweep)
    {
        _window = options.AuthWindow;
        _idleTimeout = TimeSpan.FromTicks(
            checked(_window.Ticks * EvictionIdleWindowMultiplier));
        _ipLimit = options.AuthIpLimit;
        _accountLimit = options.AuthAccountLimit;

        if (enableAutoSweep)
        {
            // Sweep on the window cadence; entries idle beyond 2×window are reclaimed.
            _sweepTimer = new Timer(
                static state => ((AuthRateLimiterPolicy)state!).SweepNow(),
                this,
                _window,
                _window);
        }
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected { get; } = static (context, _) =>
    {
        // The middleware already set 429 via RejectionStatusCode (its default is
        // 503, so RateLimiterOptions must override it); Retry-After has to come
        // from the denied lease's metadata.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter))
        {
            var seconds = Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds));
            context.HttpContext.Response.Headers["Retry-After"] =
                seconds.ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };

    public RateLimitPartition<AuthPartitionKey> GetPartition(HttpContext httpContext)
    {
        var ip = NormalizeIp(httpContext.Connection.RemoteIpAddress);
        var key = new AuthPartitionKey(ip, ResolveAccountKey(httpContext, ip));

        // The middleware caches one wrapper per partition key and calls this
        // factory only on a cache miss. The wrapper resolves child limiters on
        // every acquire (not at construction), so a cached wrapper never holds
        // a reference to a limiter that the sweep may dispose.
        return RateLimitPartition.Get(key, k => new DualFixedWindowRateLimiter(this, k, _window));
    }

    /// <summary>Child limiter count across both dimensions (diagnostics / tests).</summary>
    internal int TrackedLimiterCount => _ipLimiters.Count + _accountLimiters.Count;

    /// <summary>
    /// Evicts idle child limiters. Skips any entry whose active-use count is
    /// non-zero, so a limiter currently being acquired is never disposed.
    /// </summary>
    internal void SweepNow()
    {
        SweepDictionary(_ipLimiters);
        SweepDictionary(_accountLimiters);
    }

    /// <summary>Test hook: hold entries with ActiveCount &gt; 0 until the returned handle is disposed.</summary>
    internal IDisposable HoldEntriesForTest(string ip, string accountKey)
    {
        var ipEntry = AcquireEntry(_ipLimiters, ip, _ipLimit);
        var accountEntry = AcquireEntry(_accountLimiters, accountKey, _accountLimit);
        return new HoldHandle(() =>
        {
            ReleaseEntry(accountEntry);
            ReleaseEntry(ipEntry);
        });
    }

    /// <summary>Test hook: backdate last-use timestamps past the idle timeout.</summary>
    internal void ForceIdleForTest()
    {
        var idleTicks = (long)((_idleTimeout + TimeSpan.FromMilliseconds(50)).TotalSeconds * Stopwatch.Frequency);
        BackdateIdle(_ipLimiters, idleTicks);
        BackdateIdle(_accountLimiters, idleTicks);
    }

    public void Dispose()
    {
        _sweepTimer?.Dispose();
        DisposeAll(_ipLimiters);
        DisposeAll(_accountLimiters);
    }

    private static void BackdateIdle(ConcurrentDictionary<string, LimiterEntry> dict, long idleTicks)
    {
        var target = Stopwatch.GetTimestamp() - idleTicks;
        foreach (var entry in dict.Values)
        {
            lock (entry)
            {
                entry.LastUseTimestamp = target;
            }
        }
    }

    private void SweepDictionary(ConcurrentDictionary<string, LimiterEntry> dict)
    {
        foreach (var key in dict.Keys)
        {
            if (!dict.TryGetValue(key, out var entry))
            {
                continue;
            }

            lock (entry)
            {
                if (entry.Disposed || entry.ActiveCount > 0 || !IsIdle(entry))
                {
                    continue;
                }

                // Remove only this instance so a concurrent GetOrAdd that already
                // installed a fresh entry for the same key is left untouched.
                var removed = ((ICollection<KeyValuePair<string, LimiterEntry>>)dict)
                    .Remove(new KeyValuePair<string, LimiterEntry>(key, entry));
                if (!removed)
                {
                    continue;
                }

                entry.Disposed = true;
                entry.Limiter.Dispose();
            }
        }
    }

    private static void DisposeAll(ConcurrentDictionary<string, LimiterEntry> dict)
    {
        foreach (var entry in dict.Values)
        {
            lock (entry)
            {
                if (!entry.Disposed)
                {
                    entry.Disposed = true;
                    entry.Limiter.Dispose();
                }
            }
        }

        dict.Clear();
    }

    private bool IsIdle(LimiterEntry entry)
    {
        var elapsed = Stopwatch.GetTimestamp() - Volatile.Read(ref entry.LastUseTimestamp);
        // Double division: integer truncation yields 0 for sub-second idle when
        // Stopwatch.Frequency is 1e9 (Linux), which would never pass the timeout.
        var idle = TimeSpan.FromSeconds((double)elapsed / Stopwatch.Frequency);
        return idle > _idleTimeout;
    }

    /// <summary>
    /// Returns an entry with ActiveCount incremented and last-use refreshed.
    /// If the sweep disposed the entry while this call waited for its lock,
    /// the stale reference is dropped and a fresh entry is created.
    /// </summary>
    private LimiterEntry AcquireEntry(ConcurrentDictionary<string, LimiterEntry> dict, string key, int permitLimit)
    {
        while (true)
        {
            var entry = dict.GetOrAdd(key, _ => new LimiterEntry(CreateLimiter(permitLimit, _window)));
            lock (entry)
            {
                if (!entry.Disposed)
                {
                    entry.ActiveCount++;
                    entry.LastUseTimestamp = Stopwatch.GetTimestamp();
                    return entry;
                }
            }

            // Evicted after GetOrAdd returned it; drop the stale instance only
            // if it is still the mapped value (never remove a replacement).
            ((ICollection<KeyValuePair<string, LimiterEntry>>)dict)
                .Remove(new KeyValuePair<string, LimiterEntry>(key, entry));
        }
    }

    private static void ReleaseEntry(LimiterEntry entry)
    {
        lock (entry)
        {
            entry.ActiveCount--;
        }
    }

    private static string ResolveAccountKey(HttpContext context, string ip)
    {
        if (context.Items.TryGetValue(LoginUserNameItemKey, out var value)
            && value is string userName
            && userName.Length > 0)
        {
            return "u:" + userName;
        }

        // Refresh tokens are opaque and single-use (ADR-004 rotation), so the
        // account behind a refresh cannot be identified here without decoding
        // the token; refresh is therefore limited per IP plus this fallback
        // bucket. Malformed login bodies land here too, which keeps
        // unidentifiable attempts self-limiting per IP.
        return "ip:" + ip;
    }

    private static string NormalizeIp(IPAddress? address)
    {
        if (address is null)
        {
            return "unknown";
        }

        // Dual-stack sockets report IPv4 peers as ::ffff:x.y.z.w; normalize so
        // socket and forwarded views of the same client share one bucket.
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.ToString();
    }

    private static FixedWindowRateLimiter CreateLimiter(int permitLimit, TimeSpan window) =>
        new(new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0, // reject immediately; a queue would delay the 429
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            // These children are not in the framework's partitioned cache, so
            // they must replenish themselves.
            AutoReplenishment = true,
        });

    private sealed class LimiterEntry
    {
        public LimiterEntry(FixedWindowRateLimiter limiter)
        {
            Limiter = limiter;
            LastUseTimestamp = Stopwatch.GetTimestamp();
        }

        public FixedWindowRateLimiter Limiter { get; }

        public long LastUseTimestamp;

        public int ActiveCount;

        public bool Disposed;
    }

    private sealed class HoldHandle(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    /// <summary>
    /// Acquires the IP window first, then the account window. If the account
    /// window denies, the IP permit stays consumed so every attempt counts
    /// against the IP ceiling as well.
    ///
    /// Child limiters are resolved through the policy on every acquire and held
    /// with ActiveCount &gt; 0 for the duration, so the sweep cannot dispose a
    /// limiter mid-acquire. A cached wrapper therefore never pins a disposed
    /// child (A0-2b).
    /// </summary>
    private sealed class DualFixedWindowRateLimiter : RateLimiter
    {
        private readonly AuthRateLimiterPolicy _policy;
        private readonly AuthPartitionKey _key;
        private readonly TimeSpan _window;
        private long _lastAccessTimestamp = Stopwatch.GetTimestamp();

        public DualFixedWindowRateLimiter(
            AuthRateLimiterPolicy policy,
            AuthPartitionKey key,
            TimeSpan window)
        {
            _policy = policy;
            _key = key;
            _window = window;
        }

        // Reported so the framework's heartbeat evicts idle partition wrappers
        // after 10s; children live in the policy's dictionaries and are swept
        // independently once idle past 2×window.
        public override TimeSpan? IdleDuration
        {
            get
            {
                var elapsed = Stopwatch.GetTimestamp() - Volatile.Read(ref _lastAccessTimestamp);
                return TimeSpan.FromSeconds((double)elapsed / Stopwatch.Frequency);
            }
        }

        public override RateLimiterStatistics? GetStatistics() => null;

        protected override RateLimitLease AttemptAcquireCore(int permitCount)
        {
            Volatile.Write(ref _lastAccessTimestamp, Stopwatch.GetTimestamp());

            var ipEntry = _policy.AcquireEntry(_policy._ipLimiters, _key.Ip, _policy._ipLimit);
            try
            {
                var ipLease = ipEntry.Limiter.AttemptAcquire(permitCount);
                if (!ipLease.IsAcquired)
                {
                    return new DualLease(false, null, null, RetryAfterOf(ipLease));
                }

                var accountEntry = _policy.AcquireEntry(_policy._accountLimiters, _key.Account, _policy._accountLimit);
                try
                {
                    var accountLease = accountEntry.Limiter.AttemptAcquire(permitCount);
                    if (!accountLease.IsAcquired)
                    {
                        ipLease.Dispose();
                        return new DualLease(false, null, null, RetryAfterOf(accountLease));
                    }

                    return new DualLease(true, ipLease, accountLease, null);
                }
                finally
                {
                    ReleaseEntry(accountEntry);
                }
            }
            finally
            {
                ReleaseEntry(ipEntry);
            }
        }

        protected override ValueTask<RateLimitLease> AcquireAsyncCore(
            int permitCount,
            CancellationToken cancellationToken) =>
            new(AttemptAcquireCore(permitCount));

        protected override void Dispose(bool disposing)
        {
            // Children are owned by the policy's dictionaries, not this wrapper.
        }

        private TimeSpan RetryAfterOf(RateLimitLease lease) =>
            lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan retryAfter)
                ? retryAfter
                : _window;
    }

    private sealed class DualLease : RateLimitLease
    {
        private static readonly string[] RetryAfterName = { MetadataName.RetryAfter.Name };

        private readonly RateLimitLease? _ipLease;
        private readonly RateLimitLease? _accountLease;
        private readonly TimeSpan? _retryAfter;

        public DualLease(
            bool isAcquired,
            RateLimitLease? ipLease,
            RateLimitLease? accountLease,
            TimeSpan? retryAfter)
        {
            IsAcquired = isAcquired;
            _ipLease = ipLease;
            _accountLease = accountLease;
            _retryAfter = retryAfter;
        }

        public override bool IsAcquired { get; }

        public override IEnumerable<string> MetadataNames =>
            _retryAfter.HasValue ? RetryAfterName : Array.Empty<string>();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (_retryAfter.HasValue && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = _retryAfter;
                return true;
            }

            metadata = null;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            _ipLease?.Dispose();
            _accountLease?.Dispose();
        }
    }
}
