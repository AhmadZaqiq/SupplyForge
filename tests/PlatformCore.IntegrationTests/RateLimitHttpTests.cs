using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-2 HTTP acceptance for SEC-04 / PLT-012: the real Host process with the
/// built-in RateLimiting middleware on login/refresh. Environment overrides
/// shrink the window and limits so the suite stays fast; X-Forwarded-For
/// handling is enabled with 127.0.0.1 as the only trusted proxy.
/// The host listens on the dual-stack wildcard so IPv4 clients arrive as
/// ::ffff:127.0.0.1, which exercises IP normalization; the socket bucket is
/// therefore "127.0.0.1", so the spoof test targets its own forwarded key.
/// No Mvc.Testing package: the host runs as a child process (approved package
/// set is EF Core + JwtBearer only).
/// </summary>
public sealed class RateLimitHttpTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    private const int IpLimit = 6;
    private const int AccountLimit = 3;

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private Process? _host;
    private string _baseUrl = string.Empty;
    private readonly StringBuilder _hostLog = new();

    public async Task InitializeAsync()
    {
        var dll = FindHostDll();
        var hostDir = Path.GetDirectoryName(dll)!;

        var port = GetFreePort(IPAddress.IPv6Any);
        _baseUrl = $"http://127.0.0.1:{port}";

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = hostDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(dll);
        psi.Environment["ASPNETCORE_URLS"] = $"http://[::]:{port}";
        // A0-8 / SEC-20: Development, because the Host fails fast outside it
        // while appsettings.json still holds the placeholder secret.
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["RateLimit__AuthIpLimit"] = $"{IpLimit}";
        psi.Environment["RateLimit__AuthAccountLimit"] = $"{AccountLimit}";
        psi.Environment["RateLimit__AuthWindow"] = "00:00:06";
        psi.Environment["RateLimit__EnableForwardedHeaders"] = "true";
        psi.Environment["RateLimit__TrustedProxies__0"] = "127.0.0.1";

        _host = new Process { StartInfo = psi };
        _host.OutputDataReceived += (_, e) => AppendHostLog(e.Data);
        _host.ErrorDataReceived += (_, e) => AppendHostLog(e.Data);

        if (!_host.Start())
        {
            throw new InvalidOperationException("Failed to start SupplyForge.Host process.");
        }

        _host.BeginOutputReadLine();
        _host.BeginErrorReadLine();

        for (var i = 0; i < 90; i++)
        {
            try
            {
                var response = await _client.GetAsync($"{_baseUrl}/health");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
                // Timeout before listener is ready.
            }

            await Task.Delay(500);
        }

        string logTail;
        lock (_hostLog)
        {
            logTail = _hostLog.ToString();
        }

        throw new InvalidOperationException(
            "SupplyForge.Host did not become healthy within 45s. Host log:" + Environment.NewLine + logTail);
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_host is { HasExited: false })
            {
                _host.Kill(entireProcessTree: true);
                _host.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already gone.
        }
        finally
        {
            _host?.Dispose();
            _client.Dispose();
        }

        return Task.CompletedTask;
    }

    [Fact]
    public void SEC_04_PLT_012_DefaultConfig_KeepsForwardedHeadersDisabled()
    {
        var dll = FindHostDll();
        var path = Path.Combine(Path.GetDirectoryName(dll)!, "appsettings.json");
        using var doc = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });

        var rateLimit = doc.RootElement.GetProperty("RateLimit");
        Assert.False(rateLimit.GetProperty("EnableForwardedHeaders").GetBoolean());
        Assert.True(rateLimit.GetProperty("AuthIpLimit").GetInt32() >= 1);
        Assert.True(rateLimit.GetProperty("AuthAccountLimit").GetInt32() >= 1);
        Assert.True(rateLimit.GetProperty("AuthWindow").GetString() is { Length: > 0 });
    }

    [Fact]
    public async Task SEC_04_LoginUnderLimits_Returns200()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"rl_ok_{suffix}";

        // A0-3 Step 4: direct-DB user setup (create-user policy gates POST /users).
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "RL OK");

        var login = await PostLoginAsync(userName, "10.98.0.1", "Passw0rd!x");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await CleanupAsync(userName);
    }

    [Fact]
    public async Task SEC_04_LoginOverAccountLimit_Returns429_AndAccountBucketsAreIsolated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var blockedUser = $"rl_acct_{suffix}";
        var otherUser = $"rl_other_{suffix}";

        for (var i = 1; i <= AccountLimit; i++)
        {
            var response = await PostLoginAsync(blockedUser, $"10.98.1.{i}");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostLoginAsync(blockedUser, "10.98.1.4");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // Same IP that just served a 429: a different account still passes,
        // proving the account dimension partitions independently of the IP.
        var other = await PostLoginAsync(otherUser, "10.98.1.1");
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task SEC_04_LoginOverIpLimit_Returns429_AndIpBucketsAreIsolated()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var firstUser = $"rl_ip_{suffix}";

        for (var i = 1; i <= IpLimit; i++)
        {
            // Same IP, fresh account each time: only the IP window can trip.
            var userName = i == 1 ? firstUser : $"rl_ip_{i}_{suffix}";
            var response = await PostLoginAsync(userName, "10.98.2.1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostLoginAsync($"rl_ip_last_{suffix}", "10.98.2.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // Fresh IP with an already-seen account: passes, so buckets are per IP.
        var other = await PostLoginAsync(firstUser, "10.98.3.1");
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
    }

    [Fact]
    public async Task SEC_04_RefreshOverLimit_Returns429()
    {
        // Refresh has no user name to key on, so it falls back to the per-IP
        // account bucket (limit = AccountLimit) on top of the IP window.
        for (var i = 1; i <= AccountLimit; i++)
        {
            var response = await PostRefreshAsync($"junk-token-{i}", "10.98.4.1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostRefreshAsync("junk-token-last", "10.98.4.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task SEC_04_SpoofedMultiHopForwardedFor_CannotEvadeIpLimit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // ForwardLimit=1: only the hop appended by the trusted proxy is
        // honored, so every attempt keys on the constant right-hand value
        // (10.99.99.1) no matter what the caller spoofs on the left. If the
        // left value were used instead, each request would get a fresh bucket
        // and the window would never fill.
        for (var i = 1; i <= IpLimit; i++)
        {
            var response = await PostLoginAsync(
                $"rl_spoof_{i}_{suffix}", $"10.99.100.{i}, 10.99.99.1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostLoginAsync(
            $"rl_spoof_last_{suffix}", $"10.99.100.{IpLimit + 1}, 10.99.99.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        // The right-hand (honored) key is the one that filled up ...
        var sameKey = await PostLoginAsync($"rl_spoof_same_{suffix}", "10.99.99.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, sameKey.StatusCode);

        // ... while a spoofed left-hand value keeps its own untouched bucket.
        var spoofedLeft = await PostLoginAsync($"rl_spoof_left_{suffix}", "10.99.100.1");
        Assert.Equal(HttpStatusCode.Unauthorized, spoofedLeft.StatusCode);
    }

    [Fact]
    public async Task SEC_04_SocketPeer_AndForwardedLoopback_ShareOneIpBucket()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // The dual-stack host sees IPv4 clients as ::ffff:127.0.0.1; the
        // explicit forwarded value parses as plain 127.0.0.1. If those were
        // different keys the window would never fill within IpLimit requests.
        for (var i = 1; i <= IpLimit; i++)
        {
            var userName = $"rl_loop_{i}_{suffix}";
            var response = i % 2 == 1
                ? await PostLoginAsync(userName, xff: null)
                : await PostLoginAsync(userName, "127.0.0.1");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostLoginAsync($"rl_loop_last_{suffix}", xff: null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);
    }

    [Fact]
    public async Task SEC_04_WindowReset_AllowsAttemptsAgain()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        const string xff = "10.98.5.1";

        for (var i = 1; i <= IpLimit; i++)
        {
            var response = await PostLoginAsync($"rl_win_{i}_{suffix}", xff);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await PostLoginAsync($"rl_win_last_{suffix}", xff);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        await Task.Delay(TimeSpan.FromSeconds(7)); // window under test is 6s

        var after = await PostLoginAsync($"rl_win_after_{suffix}", xff);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    private Task<HttpResponseMessage> PostLoginAsync(
        string userName,
        string? xff,
        string password = "definitely-wrong")
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/api/v1/auth/login")
        {
            Content = Json(new { userName, password }),
        };
        if (xff is not null)
        {
            request.Headers.Add("X-Forwarded-For", xff);
        }

        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> PostRefreshAsync(string refreshToken, string? xff)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_baseUrl}/api/v1/auth/refresh")
        {
            Content = Json(new { refreshToken }),
        };
        if (xff is not null)
        {
            request.Headers.Add("X-Forwarded-For", xff);
        }

        return _client.SendAsync(request);
    }

    private async Task CleanupAsync(string userName)
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;

        await using var db = new PlatformCoreDbContext(opts);
        var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == userName);
        if (user is null)
        {
            return;
        }

        // Bulk deletes: no tracked-entity affected-count races with other
        // test classes cleaning up the same tables concurrently.
        await db.RefreshTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
        await db.AuditLogs.Where(a => a.EntityId == user.Id.ToString()).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private void AppendHostLog(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_hostLog)
        {
            _hostLog.AppendLine(line);
        }
    }

    private static int GetFreePort(IPAddress address)
    {
        var listener = new System.Net.Sockets.TcpListener(address, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string FindHostDll()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    dir.FullName, "src", "Host", "SupplyForge.Host", "bin", configuration, "net10.0",
                    "SupplyForge.Host.dll");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(
            "SupplyForge.Host.dll not found above " + AppContext.BaseDirectory +
            ". Build the solution before running tests (dotnet build).");
    }
}
