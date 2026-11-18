using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SupplyForge.Host.Auth;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// HTTP acceptance against the real Host process with the standard JwtBearer
/// pipeline: A0-1 SEC-13 (401 for unauthenticated/invalid tokens), PLT-011
/// (valid JWT passes), INT-050 (X-Api-Key path next to JWT), the A0-3
/// Step 7 SEC-13 401/403 matrix over every protected endpoint, the A0-4 S1
/// BRN-011/013/014 status discrimination, and the A0-4 S3 INT-052 OpenAPI
/// security-scheme contract.
/// No Mvc.Testing package: the host is started as a child process (approved
/// package set is EF Core + JwtBearer only).
/// </summary>
public sealed class HttpAuthMatrixTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

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
        // INT-052: MapOpenApi() is mapped only in Development; the OpenAPI
        // tests below need the document endpoint.
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

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

    // Every protected Host endpoint (A0-3 surface): anonymous must get the
    // 401 challenge, authenticated-without-permission must get 403 (SEC-13).
    // Public endpoints (health, login, refresh, api-keys/me) are not listed —
    // they never 401/403 by policy; api-keys/me challenges in its own handler
    // and is asserted separately.
    public static TheoryData<string, string> ProtectedEndpoints()
    {
        return new TheoryData<string, string>
        {
            { "GET", "/api/v1/notifications" },
            { "POST", "/api/v1/notifications" },
            { "POST", "/api/v1/api-keys" },
            { "DELETE", "/api/v1/api-keys/7b3f0000-0000-4000-8000-000000000001" },
            { "POST", "/api/v1/users" },
        };
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task SEC_13_ProtectedEndpoints_Anonymous_Return401(string method, string path)
    {
        // The authorization middleware challenges before the endpoint runs:
        // 401 for every protected endpoint. A 403 here would conflate "not
        // authenticated" with "not permitted" (SEC-13).
        var response = await SendAsync(method, path, token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task SEC_13_ProtectedEndpoints_AuthenticatedWithoutPermission_Return403(string method, string path)
    {
        // A valid JWT whose subject holds no permissions: token validation
        // passes (401 here would mean the token itself was rejected) and
        // authorization denies with 403 on every protected endpoint (SEC-13).
        var response = await SendAsync(method, path, CreateValidToken());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SEC_13_PublicEndpoints_NeverPolicyProtected_LivenessOpen_KeyEndpointChallengesItself()
    {
        // Liveness stays open (explicit AllowAnonymous in the coverage scan).
        var health = await _client.GetAsync($"{_baseUrl}/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // api-keys/me is AllowAnonymous (X-Api-Key clients must pass the
        // fail-closed fallback), but its own handler challenges a missing key
        // with 401 — the policy never has to.
        var me = await _client.GetAsync($"{_baseUrl}/api/v1/api-keys/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    // --- A0-4 S1: BRN-011/013/014 status discrimination on one id-addressed
    // route. api-keys are the only load-by-id resource in A0 (no branch-owned
    // resource exists yet — Branch belongs to Master Data, A1); the real
    // foreign-branch IDOR leg of SEC-12/BRN-012 lands with the first
    // branch-owned resource. ---

    [Fact]
    public async Task BRN_014_ApiKeyRevoke_Unauthenticated_Returns401()
    {
        // Authentication precedes everything: an anonymous caller learns
        // nothing about whether the id exists (BRN-014: unauthenticated → 401,
        // never 403/404).
        var response = await SendAsync(
            "DELETE", "/api/v1/api-keys/7b3f0000-0000-4000-8000-000000000009", token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BRN_013_ApiKeyRevoke_KnownId_WithoutPermission_Returns403_WithPermission_Returns204()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminName = $"brnadm_{suffix}";
        var plainName = $"brnusr_{suffix}";
        await TestUsers.CreateAsync(adminName, "Passw0rd!x", "BRN Admin");
        await TestUsers.CreateAsync(plainName, "Passw0rd!x", "BRN User");
        var grantId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            adminName, "platform.apikey.manage");

        var adminToken = await LoginAsync(adminName);
        var plainToken = await LoginAsync(plainName);

        // Make the resource exist: the admin issues a key (known id).
        var issue = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/v1/api-keys",
            Json(new { name = $"brn-key-{suffix}", permissionCodes = new[] { "platform.user.read" } }),
            adminToken);
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);
        using var issueDoc = JsonDocument.Parse(await issue.Content.ReadAsStringAsync());
        var keyId = issueDoc.RootElement.GetProperty("id").GetGuid();

        // BRN-013: authenticated caller, known in-scope resource, missing the
        // function permission → 403 (not 401, and not 404 — the resource is
        // not the missing thing here).
        var denied = await SendAsync("DELETE", $"/api/v1/api-keys/{keyId}", plainToken);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Control: the same known id with the permission succeeds (204), so
        // the 403 above is proven to be a permission denial, not an artifact
        // of the id or route (no id-based bypass either way, BRN-003).
        var allowed = await SendAsync("DELETE", $"/api/v1/api-keys/{keyId}", adminToken);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

        await TestPermissionGrant.RemoveGrantAsync(grantId);
        await CleanupAsync(adminName, keyId);
        await CleanupAsync(plainName, Guid.Empty);
    }

    [Fact]
    public async Task BRN_011_ApiKeyRevoke_UnknownId_WithPermission_Returns404()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminName = $"brn404_{suffix}";
        await TestUsers.CreateAsync(adminName, "Passw0rd!x", "BRN 404");
        var grantId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            adminName, "platform.apikey.manage");

        var adminToken = await LoginAsync(adminName);

        // BRN-011: the caller passes the function permission, the resource
        // does not exist → 404 (existence is only ever disclosed to callers
        // who are allowed to know it).
        var response = await SendAsync(
            "DELETE", "/api/v1/api-keys/7b3f0000-0000-4000-8000-00000000000a", adminToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await TestPermissionGrant.RemoveGrantAsync(grantId);
        await CleanupAsync(adminName, Guid.Empty);
    }

    // --- A0-4 S3: INT-052 OpenAPI from code, ADR-006 §8–9 — both auth
    // schemes documented, security applied per endpoint metadata. ---

    [Fact]
    public async Task INT_052_OpenApi_DocumentsBearerAndXApiKeySecuritySchemes()
    {
        using var doc = await GetOpenApiDocumentAsync();

        var schemes = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");

        // Interactive users: Authorization: Bearer <access-token> (ADR-004/006).
        var bearer = schemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        // Integration Clients: frozen X-Api-Key header (ADR-009).
        var apiKey = schemes.GetProperty("X-Api-Key");
        Assert.Equal("apiKey", apiKey.GetProperty("type").GetString());
        Assert.Equal("X-Api-Key", apiKey.GetProperty("name").GetString());
        Assert.Equal("header", apiKey.GetProperty("in").GetString());

        // No global requirement: security is decided per operation, so the
        // public endpoints stay honestly unsecured (asserted below).
        Assert.False(doc.RootElement.TryGetProperty("security", out _));
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task INT_052_OpenApi_ProtectedOperations_DeclareBearerRequirement(string method, string path)
    {
        // The OpenAPI contract must mirror the endpoint's authorization
        // metadata (PLT-022): every policy-protected operation carries the
        // Bearer requirement, matching its observed 401/403 behavior.
        using var doc = await GetOpenApiDocumentAsync();

        // The matrix addresses a concrete id; the document keys the route
        // template — map any GUID segment onto {id} for the lookup.
        var templatePath = Regex.Replace(
            path,
            "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            "{id}");
        var operation = doc.RootElement.GetProperty("paths").GetProperty(templatePath)
            .GetProperty(method.ToLowerInvariant());
        var security = operation.GetProperty("security");
        Assert.Single(security.EnumerateArray());
        var requirement = security[0];
        Assert.True(requirement.TryGetProperty("Bearer", out var scopes));
        Assert.Empty(scopes.EnumerateArray());
        Assert.False(requirement.TryGetProperty("X-Api-Key", out _));
    }

    [Fact]
    public async Task INT_052_OpenApi_PublicOperations_DeclareNoSecurity_AndApiKeyRouteUsesXApiKey()
    {
        using var doc = await GetOpenApiDocumentAsync();
        var paths = doc.RootElement.GetProperty("paths");

        // Liveness, login, refresh: no requirement — the contract matches
        // their observed behavior (200/400, never 401 by policy).
        foreach (var (path, method) in new[]
        {
            ("/health", "get"),
            ("/api/v1/auth/login", "post"),
            ("/api/v1/auth/refresh", "post"),
        })
        {
            var operation = paths.GetProperty(path).GetProperty(method);
            Assert.False(operation.TryGetProperty("security", out _),
                $"{method.ToUpperInvariant()} {path} must declare no security requirement");
        }

        // The one X-Api-Key-authenticated route declares that scheme (its
        // handler challenges the missing header itself, AllowAnonymous only
        // w.r.t. the fail-closed JWT policy).
        var me = paths.GetProperty("/api/v1/api-keys/me").GetProperty("get");
        var meSecurity = me.GetProperty("security");
        Assert.Single(meSecurity.EnumerateArray());
        Assert.True(meSecurity[0].TryGetProperty("X-Api-Key", out _));
        Assert.False(meSecurity[0].TryGetProperty("Bearer", out _));
    }

    private async Task<JsonDocument> GetOpenApiDocumentAsync()
    {
        var response = await _client.GetAsync($"{_baseUrl}/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private async Task<string> LoginAsync(string userName)
    {
        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));
        return accessToken!;
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("tampered")]
    [InlineData("expired")]
    [InlineData("wrong_issuer")]
    [InlineData("wrong_audience")]
    [InlineData("alg_none")]
    public async Task SEC_13_InvalidToken_Kind_Returns401(string kind)
    {
        var token = kind switch
        {
            "malformed" => "not.a-jwt-token",
            "tampered" => Tamper(CreateValidToken()),
            "expired" => Mint(_issuer, _audience, _secret,
                DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-5)),
            "wrong_issuer" => Mint("someone-else", _audience, _secret,
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(14)),
            "wrong_audience" => Mint(_issuer, "wrong-audience", _secret,
                DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(14)),
            "alg_none" => AlgNone(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown kind"),
        };

        var response = await GetWithBearerAsync("/api/v1/notifications", token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PLT_011_ValidJwt_IsAuthenticated_ButWithoutPermission_Returns403()
    {
        // A valid JWT passes token validation — 401 here would mean the token
        // itself was rejected. The minted subject exists in no role bundle, so
        // A0-3 Step 2 denies authorization with 403 (SEC-13: authenticated
        // without permission ⇒ 403; the 200-with-grant path lives in
        // FailClosedAuthHttpTests.SEC_10_PermPolicy_WithGrant_Allows_AuthenticatedCaller).
        var token = CreateValidToken();
        Assert.Equal(3, token.Split('.').Length);

        var response = await GetWithBearerAsync("/api/v1/notifications", token);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task INT_050_HttpFlow_JwtLogin_ThenApiKeyScope_StillWorks()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"http_{suffix}";

        // A0-3 Step 4: direct-DB user setup (create-user policy gates POST /users).
        await TestUsers.CreateAsync(userName, "Passw0rd!x", "HTTP IT");

        // A0-3 Step 2: key management endpoint requires platform.apikey.manage.
        var grantRoleId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            userName, "platform.apikey.manage");

        var login = await _client.PostAsync(
            $"{_baseUrl}/api/v1/auth/login",
            Json(new { userName, password = "Passw0rd!x" }));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        using var loginDoc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var accessToken = loginDoc.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(accessToken));

        // Issue an API key using the JWT bearer token (ADR-006 dual auth).
        var issue = await SendWithBearerAsync(
            HttpMethod.Post,
            "/api/v1/api-keys",
            Json(new { name = $"http-key-{suffix}", permissionCodes = new[] { "platform.user.read" } }),
            accessToken!);
        Assert.Equal(HttpStatusCode.Created, issue.StatusCode);

        using var keyDoc = JsonDocument.Parse(await issue.Content.ReadAsStringAsync());
        var apiKeyId = keyDoc.RootElement.GetProperty("id").GetGuid();
        var apiKey = keyDoc.RootElement.GetProperty("secret").GetString();
        Assert.False(string.IsNullOrWhiteSpace(apiKey));

        // X-Api-Key path (frozen header, ADR-009) still authenticates.
        var me = await GetWithApiKeyAsync("/api/v1/api-keys/me", apiKey!);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var badKey = await GetWithApiKeyAsync("/api/v1/api-keys/me", "sfk_invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, badKey.StatusCode);

        await TestPermissionGrant.RemoveGrantAsync(grantRoleId);
        await CleanupAsync(userName, apiKeyId);
    }

    private async Task CleanupAsync(string userName, Guid apiKeyId)
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
        await db.ApiKeyScopes.Where(s => s.ApiKeyId == apiKeyId).ExecuteDeleteAsync();
        await db.ApiKeys.Where(k => k.Id == apiKeyId).ExecuteDeleteAsync();
        await db.AuditLogs
            .Where(a => a.EntityId == user.Id.ToString() || a.EntityId == apiKeyId.ToString())
            .ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
    }

    private Task<HttpResponseMessage> SendAsync(string method, string path, string? token)
    {
        var httpMethod = method switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "DELETE" => HttpMethod.Delete,
            _ => throw new ArgumentOutOfRangeException(nameof(method), method, "unsupported matrix method"),
        };

        var request = new HttpRequestMessage(httpMethod, _baseUrl + path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        // No body: authorization short-circuits before model binding, so an
        // empty request still proves the 401/403 status under test.
        return _client.SendAsync(request);
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

    private Task<HttpResponseMessage> GetWithApiKeyAsync(string path, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _baseUrl + path);
        request.Headers.Add("X-Api-Key", apiKey);
        return _client.SendAsync(request);
    }

    private static StringContent Json(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private string CreateValidToken() =>
        new JwtAccessTokenIssuer(_issuer, _audience, _secret, TimeProvider.System)
            .Issue(Guid.NewGuid(), "matrix@test.local").Token;

    private static string Mint(
        string issuer, string audience, string secret, DateTimeOffset notBefore, DateTimeOffset expires)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = new JwtSecurityToken(
            issuer,
            audience,
            new[] { new System.Security.Claims.Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()) },
            notBefore.UtcDateTime,
            expires.UtcDateTime,
            new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                SecurityAlgorithms.HmacSha256));
        return handler.WriteToken(token);
    }

    private static string Tamper(string token)
    {
        var parts = token.Split('.');
        var payload = parts[1];
        var flipped = (payload[0] == 'A' ? 'B' : 'A') + payload[1..];
        return parts[0] + "." + flipped + "." + parts[2];
    }

    private string AlgNone()
    {
        static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var exp = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds();
        var header = Base64Url(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var payload = Base64Url(Encoding.UTF8.GetBytes(
            $"{{\"sub\":\"{Guid.NewGuid()}\",\"iss\":\"{_issuer}\",\"aud\":\"{_audience}\",\"exp\":{exp}}}"));
        return header + "." + payload + ".";
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

    private void ReadHostAuthConfig(string hostDir)
    {
        var path = Path.Combine(hostDir, "appsettings.json");
        // appsettings.json carries // comments (RateLimit section).
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
