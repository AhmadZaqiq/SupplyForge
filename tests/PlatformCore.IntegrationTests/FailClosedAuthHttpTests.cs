using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-3 Step 1 HTTP acceptance: fail-closed authorization defaults with
/// explicit AllowAnonymous on intentionally public endpoints only
/// (login, refresh, health) plus create-user / named perm: policies.
/// Child-process Host (no Mvc.Testing — approved package set).
/// </summary>
public sealed class FailClosedAuthHttpTests : IAsyncLifetime
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private Process? _host;
    private string _baseUrl = string.Empty;
    private string _secret = string.Empty;
    private string _issuer = string.Empty;
    private string _audience = string.Empty;
    private readonly StringBuilder _hostLog = new();

    public async Task InitializeAsync()
    {
        var dll = FindHostDll();
        var hostDir = Path.GetDirectoryName(dll)!;
        ReadHostAuthConfig(hostDir);

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
    public async Task SEC_10_Health_AllowAnonymous_StillWorks()
    {
        var health = await _client.GetAsync($"{_baseUrl}/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task SEC_10_LoginRefresh_AllowAnonymous_StillWork()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"fc_{suffix}";

        // A0-3 Step 4: POST /users is no longer an anonymous fixture — the
        // create-user policy gates it once any account exists; set the user
        // up directly instead.
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "FC");

        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var refreshToken = loginDoc.RootElement.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(refreshToken));

        var refresh = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/refresh",
            Json(new { refreshToken }));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        await CleanupAsync(userName);
    }

    [Fact]
    public async Task SEC_13_CreateUser_Anonymous_Returns401_WhenUsersExist()
    {
        // A0-3 Step 4: the named create-user policy now applies its bootstrap
        // handler — anonymous creation is allowed only while the Users table
        // is empty, and the shared test database has users (401, SEC-13).
        // The empty-window allow-path cannot be HTTP-tested without emptying
        // a shared database, so it is proven at unit level
        // (FailClosedAuthorizationTests / CreateUserAuthorizationTests).
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var created = await _client.PostAsync(
            $"{_baseUrl}/api/v1/users",
            Json(new { userName = $"fc_cu_{suffix}", email = $"{suffix}@test.local", password = "Passw0rd!x", displayName = "CU" }));
        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
    }

    [Fact]
    public async Task SEC_13_CreateUser_AuthenticatedWithoutManagePermission_Returns403()
    {
        // A0-3 Step 4 / SEC-13: past the bootstrap window an authenticated
        // caller without platform.user.manage is forbidden (403 — the token
        // itself is valid, so 401 would be wrong).
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"fc_cu403_{suffix}";
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "NoManage");

        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/users")
        {
            Content = Json(new { userName = $"fc_cu403_new_{suffix}", email = $"new_{suffix}@test.local", password = "Passw0rd!x", displayName = "New" }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var created = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, created.StatusCode);

        await CleanupAsync(userName);
    }

    [Fact]
    public async Task SEC_10_PermPolicy_WithGrant_Allows_AuthenticatedCaller()
    {
        // A0-3 Step 2: real permission check — the user holds the grant through
        // a role bundle (PLT-020), so GET succeeds.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"fc_perm_{suffix}";

        // A0-3 Step 4: direct-DB user setup (create-user policy gates POST /users).
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "Perm");

        var roleId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            userName, "platform.notification.read");

        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var get = await GetWithBearerAsync("/api/v1/notifications", accessToken!);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        await TestPermissionGrant.RemoveGrantAsync(roleId);
        await CleanupAsync(userName);
    }

    [Fact]
    public async Task SEC_13_PermPolicy_AuthenticatedWithoutPermission_Returns403()
    {
        // A0-3 Step 2 / SEC-13: authenticated caller with NO role grant is
        // forbidden — perm:* is no longer "any valid JWT" (401 would mean the
        // token itself was rejected).
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"fc_403_{suffix}";

        // A0-3 Step 4: direct-DB user setup (create-user policy gates POST /users).
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "NoPerm");

        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        var get = await GetWithBearerAsync("/api/v1/notifications", accessToken!);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);

        await CleanupAsync(userName);
    }

    [Fact]
    public async Task SEC_10_Fallback_MetadataLess_Anonymous_Denied()
    {
        // No production metadata-less route is exposed; the fail-closed fallback
        // is proven at unit level (FailClosedAuthorizationTests). Here we assert
        // an unauthenticated call to a perm:-gated route is still 401.
        var get = await _client.GetAsync($"{_baseUrl}/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    [Fact]
    public async Task SEC_10_Fallback_MetadataLess_Authenticated_Denied_ViaBareStyle()
    {
        // Authenticated caller without a named policy that allows them cannot
        // use DefaultPolicy-as-allow-any: perm: routes still require a JWT
        // (challenge/forbid). Bare RequireAuthorization is rejected by coverage;
        // DefaultPolicy fail-closed is proven in unit tests. Here: bad JWT → 401.
        var get = await GetWithBearerAsync("/api/v1/notifications", "not.a-jwt");
        Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
    }

    private async Task CleanupAsync(string userName)
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(
                "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True",
                sql => sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    PlatformCoreDbContext.Schema))
            .Options;

        await using var db = new PlatformCoreDbContext(opts);
        var user = await db.Users.FirstOrDefaultAsync(x => x.UserName == userName);
        if (user is null)
        {
            return;
        }

        await db.RefreshTokens.Where(t => t.UserId == user.Id).ExecuteDeleteAsync();
        await db.AuditLogs
            .Where(a => a.EntityId == user.Id.ToString())
            .ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
    }

    private Task<HttpResponseMessage> GetWithBearerAsync(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return _client.SendAsync(request);
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

    private void ReadHostAuthConfig(string hostDir)
    {
        var path = Path.Combine(hostDir, "appsettings.json");
        using var doc = JsonDocument.Parse(
            File.ReadAllText(path),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var auth = doc.RootElement.GetProperty("Auth");
        _issuer = auth.GetProperty("Issuer").GetString()!;
        _audience = auth.GetProperty("Audience").GetString()!;
        _secret = auth.GetProperty("AccessTokenSecret").GetString()!;
    }

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

        throw new FileNotFoundException(
            "SupplyForge.Host.dll not found above " + AppContext.BaseDirectory +
            ". Build the solution before running tests (dotnet build).");
    }
}
