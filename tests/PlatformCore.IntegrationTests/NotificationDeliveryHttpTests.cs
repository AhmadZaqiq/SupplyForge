using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// ADR-008 round-trip legs that need a failure-injected email adapter, run
/// against a second real Host process (NFR-050): PLT-052 — a failing raise
/// still answers 201 with the failure recorded, the async retry worker
/// (INT-003) dead-letters the row as Failed, visible in the read API; and
/// TD-2 — manual mark-sent recovers the Failed row. Env:
/// Email:SimulateFailures=true, MaxAttempts=2, near-instant backoff.
/// No Mvc.Testing package: the host is a child process (approved package
/// set is EF Core + JwtBearer only), same pattern as HttpAuthMatrixTests.
/// </summary>
public sealed class NotificationDeliveryHttpTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private Process? _host;
    private string _baseUrl = string.Empty;
    private readonly StringBuilder _hostLog = new();

    public async Task InitializeAsync()
    {
        var dll = FindHostDll();
        var hostDir = Path.GetDirectoryName(dll)!;

        var port = GetFreePort();
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
        psi.Environment["ASPNETCORE_URLS"] = _baseUrl;
        // A0-8 / SEC-20: Development, because the Host fails fast outside it
        // while appsettings.json still holds the placeholder secret.
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        // ADR-008 failure injection: every fake send throws; two attempts
        // with a near-instant backoff so the worker dead-letters within the
        // test budget instead of the default 5s/3-attempt schedule.
        psi.Environment["Email__SimulateFailures"] = "true";
        psi.Environment["Email__MaxAttempts"] = "2";
        psi.Environment["Email__RetryBaseDelay"] = "00:00:00.2";
        psi.Environment["Email__RetryInterval"] = "00:00:00.2";

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
    public async Task PLT_052_FailureInjection_RaiseStill201_AsyncRetryDeadLettersAsFailed_VisibleInReadApi()
    {
        var (token, userName, raiseGrantId, readGrantId) = await CreateNotifyingUserAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        // PLT-052: the inline attempt fails against the injected sink, but
        // the failure is recorded state — the raise answers 201, never 500.
        var raise = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/v1/notifications",
            Json(new { notificationType = "approval.requested", title = $"Fail {suffix}", body = "injected" }),
            token);
        Assert.Equal(HttpStatusCode.Created, raise.StatusCode);
        using var raiseDoc = JsonDocument.Parse(await raise.Content.ReadAsStringAsync());
        var id = raiseDoc.RootElement.GetProperty("id").GetGuid();

        // INT-003: the worker retries once (attempt 2 == MaxAttempts) and
        // dead-letters the row as Failed — visible through the read API.
        var item = await WaitForDeliveryStatusAsync(token, id, "Failed");
        Assert.Equal(2, item.GetProperty("deliveryAttempts").GetInt32());
        Assert.Contains(
            "Simulated delivery failure",
            item.GetProperty("lastDeliveryError").GetString()!);
        Assert.True(
            item.GetProperty("sentAt").ValueKind == JsonValueKind.Null,
            "a dead-lettered row must not claim a send time.");

        await TestPermissionGrant.RemoveGrantAsync(raiseGrantId);
        await TestPermissionGrant.RemoveGrantAsync(readGrantId);
        await CleanupNotificationAsync(id);
        await CleanupUserAsync(userName);
    }

    [Fact]
    public async Task TD_2_ManualMarkSent_OnFailedRow_RecoversToSent_VisibleInReadApi()
    {
        var (token, userName, raiseGrantId, readGrantId) = await CreateNotifyingUserAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var raise = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/v1/notifications",
            Json(new { notificationType = "approval.requested", title = $"Mark {suffix}", body = "dead-letter" }),
            token);
        Assert.Equal(HttpStatusCode.Created, raise.StatusCode);
        using var raiseDoc = JsonDocument.Parse(await raise.Content.ReadAsStringAsync());
        var id = raiseDoc.RootElement.GetProperty("id").GetGuid();

        // Wait for the dead-letter so mark-sent recovers a genuinely Failed
        // row — the TD-2 case manual mark-sent exists for.
        await WaitForDeliveryStatusAsync(token, id, "Failed");

        var mark = await SendWithBearerAsync(
            HttpMethod.Post, $"/api/v1/notifications/{id}/mark-sent", new StringContent(string.Empty), token);
        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);
        using var markDoc = JsonDocument.Parse(await mark.Content.ReadAsStringAsync());
        Assert.Equal("Sent", markDoc.RootElement.GetProperty("deliveryStatus").GetString());

        var after = await GetItemAsync(token, id);
        Assert.Equal("Sent", after.GetProperty("deliveryStatus").GetString());
        Assert.NotNull(after.GetProperty("sentAt").GetString());
        Assert.True(after.GetProperty("lastDeliveryError").ValueKind == JsonValueKind.Null);

        // Idempotent repeat (TD-2 is an operator workflow, not a trap).
        var repeat = await SendWithBearerAsync(
            HttpMethod.Post, $"/api/v1/notifications/{id}/mark-sent", new StringContent(string.Empty), token);
        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);

        // Unknown id → 404 (the permission gate still precedes it: same
        // token, so 404 here proves existence is the only discriminator).
        var missing = await SendWithBearerAsync(
            HttpMethod.Post,
            $"/api/v1/notifications/{Guid.NewGuid()}/mark-sent",
            new StringContent(string.Empty),
            token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        await TestPermissionGrant.RemoveGrantAsync(raiseGrantId);
        await TestPermissionGrant.RemoveGrantAsync(readGrantId);
        await CleanupNotificationAsync(id);
        await CleanupUserAsync(userName);
    }

    /// <summary>
    /// Creates a user holding both notification permissions and returns its
    /// bearer token (direct-DB setup: create-user policy gates POST /users).
    /// </summary>
    private async Task<(string Token, string UserName, Guid RaiseGrantId, Guid ReadGrantId)>
        CreateNotifyingUserAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"delv_{suffix}";
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "Delivery");
        var raiseGrantId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            userName, "platform.notification.raise");
        var readGrantId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            userName, "platform.notification.read");

        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        return (token!, userName, raiseGrantId, readGrantId);
    }

    /// <summary>
    /// Polls the read API until the row reaches <paramref name="expected"/>
    /// (the worker runs on a 0.2s interval; 80 × 250ms = 20s budget).
    /// </summary>
    private async Task<JsonElement> WaitForDeliveryStatusAsync(
        string token, Guid id, string expected)
    {
        for (var i = 0; i < 80; i++)
        {
            var item = await GetItemAsync(token, id);
            if (item.ValueKind != JsonValueKind.Undefined &&
                item.GetProperty("deliveryStatus").GetString() == expected)
            {
                return item;
            }

            await Task.Delay(250);
        }

        Assert.Fail($"notification {id} never reached deliveryStatus={expected} within 20s.");
        return default; // unreachable — Assert.Fail throws.
    }

    private async Task<JsonElement> GetItemAsync(string token, Guid id)
    {
        var read = await GetWithBearerAsync("/api/v1/notifications", token);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var doc = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        var item = doc.RootElement.EnumerateArray()
            .FirstOrDefault(x => x.GetProperty("id").GetGuid() == id);
        return item.ValueKind == JsonValueKind.Undefined ? item : item.Clone();
    }

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

    private async Task CleanupNotificationAsync(Guid notificationId)
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;

        await using var db = new PlatformCoreDbContext(opts);
        await db.Notifications.Where(n => n.Id == notificationId).ExecuteDeleteAsync();
    }

    private async Task CleanupUserAsync(string userName)
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

    private Task<HttpResponseMessage> GetWithBearerAsync(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> SendWithBearerAsync(
        HttpMethod method, string path, HttpContent content, string token)
    {
        var request = new HttpRequestMessage(method, _baseUrl + path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
    }

    private static StringContent Json(object body) =>
        new(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
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

        throw new FileNotFoundException("SupplyForge.Host.dll not found above " + AppContext.BaseDirectory);
    }
}
