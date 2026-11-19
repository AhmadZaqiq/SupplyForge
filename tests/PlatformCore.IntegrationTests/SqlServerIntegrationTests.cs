using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using System.Text.Json;
using SupplyForge.Host.Auth;
using SupplyForge.Host.Data;
using SupplyForge.Integrations.Infrastructure.Data;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Application.Auth;
using SupplyForge.PlatformCore.Infrastructure.Application.Integrations;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Services;
using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Idempotency;
using SupplyForge.Shared.Jobs;
using SupplyForge.Shared.Notifications;
using SupplyForge.Shared.Sequencing;
using SupplyForge.Shared.Outbox;
using SupplyForge.Integrations.Infrastructure.Outbox;
using SupplyForge.PlatformCore.Application.Email;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// Real SQL Server integration tests against local Docker Compose DB (NFR-050).
/// Requires: docker compose up -d sqlserver.
/// </summary>
public class SqlServerIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    // ADR-004 JWT parameters used by tests that need a real access token.
    private const string TestIssuer = "supplyforge";
    private const string TestAudience = "supplyforge-api";
    private const string TestJwtSecret = "integration-test-signing-secret-0123456789!";

    private static bool _databaseReady;

    public async Task InitializeAsync()
    {
        if (_databaseReady)
        {
            return;
        }

        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;

        await using var db = new PlatformCoreDbContext(opts);
        if (db.Database.CanConnect())
        {
            _databaseReady = true;
        }
        else
        {
            throw new InvalidOperationException(
                "SQL Server not reachable. Start it with: docker compose up -d sqlserver");
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

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

    private static AuthService CreateAuth(PlatformCoreDbContext db)
    {
        // A0-6 S4: CreateUserAsync now runs inside the ADR-003 ambient
        // session — the production factory joins this very context (plus the
        // Integrations context) so the test exercises the same composition
        // the Host DI registers.
        var integrationsOpts = new DbContextOptionsBuilder<IntegrationsDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", IntegrationsDbContext.Schema))
            .Options;

        return new AuthService(
            db,
            new PlatformSharedServices(db, TimeProvider.System),
            TimeProvider.System,
            new JwtAccessTokenIssuer(TestIssuer, TestAudience, TestJwtSecret, TimeProvider.System),
            new PermissionResolver(db),
            new AmbientDbSessionFactory(db, new IntegrationsDbContext(integrationsOpts)));
    }

    [Fact]
    public async Task NFR_050_PlatformCore_CanConnectAndQueryUsers()
    {
        await using var db = CreatePlatformDb();
        Assert.True(await db.Database.CanConnectAsync());
        var count = await db.Users.CountAsync();
        Assert.True(count >= 0);
    }

    [Fact]
    public async Task PLT_040_AuditWriter_PersistsRow()
    {
        await using var db = CreatePlatformDb();
        var services = new PlatformSharedServices(db, TimeProvider.System);
        await services.WriteAsync("test", "tester", "test.action", "Entity", "1", null, "{\"ok\":true}");

        var last = await db.AuditLogs.OrderByDescending(x => x.OccurredAt).FirstAsync();
        Assert.Equal("test.action", last.Action);
        Assert.Equal("tester", last.ActorId);

        db.AuditLogs.Remove(last);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_073_DocumentSequence_AllocatesMonotonicNumbers()
    {
        await using var db = CreatePlatformDb();
        var services = new PlatformSharedServices(db, TimeProvider.System);
        const string scope = "test.seq";
        var existing = await db.DocumentSequences.Where(x => x.Scope == scope).ToListAsync();
        db.DocumentSequences.RemoveRange(existing);
        await db.SaveChangesAsync();

        var n1 = await services.NextAsync(scope, "PO-", 6);
        var n2 = await services.NextAsync(scope, "PO-", 6);

        Assert.Equal("PO-000001", n1);
        Assert.Equal("PO-000002", n2);

        await using var db2 = CreatePlatformDb();
        var row = await db2.DocumentSequences.FirstAsync(x => x.Scope == scope);
        db2.DocumentSequences.Remove(row);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task OUT_01_Outbox_Enqueue_PersistsPendingMessage()
    {
        await using var db = CreateIntegrationsDb();
        var outbox = new Outbox(db, TimeProvider.System);
        await outbox.EnqueueAsync("platform.test_event", "{\"value\":1}", version: 1);

        var msg = await db.OutboxMessages.OrderByDescending(x => x.OccurredAt).FirstAsync();
        Assert.Equal("platform.test_event", msg.EventType);
        Assert.Equal("Pending", msg.Status);
        Assert.Equal(1, msg.Version);

        db.OutboxMessages.Remove(msg);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_010_CreateUser_ThenLogin_Succeeds()
    {
        await using var db = CreatePlatformDb();
        var auth = CreateAuth(db);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"it_{suffix}";

        // A0-3 Step 4: past the bootstrap window, creation needs an actor
        // holding platform.user.manage (granted through a role bundle).
        var (managerId, grantId) = await CreateManagerAsync(suffix);
        var created = await auth.CreateUserAsync(
            userName, $"{suffix}@test.local", "Passw0rd!x", "IT User", managerId);
        Assert.True(created.Success);
        Assert.NotEqual(Guid.Empty, created.UserId);

        var login = await auth.LoginAsync(userName, "Passw0rd!x");
        Assert.True(login.Success);
        Assert.NotNull(login.AccessToken);
        Assert.NotNull(login.RefreshToken);

        // A0-1 (PLT-011): access token is a JWT validated with the standard handler.
        Assert.Equal(3, login.AccessToken!.Split('.').Length);
        Assert.NotNull(login.AccessTokenExpiresAt);
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(
            login.AccessToken,
            JwtAuthDefaults.CreateValidationParameters(TestJwtSecret, TestIssuer, TestAudience));
        Assert.True(validation.IsValid, validation.Exception?.ToString());
        var jwt = Assert.IsType<JsonWebToken>(validation.SecurityToken);
        Assert.Equal(created.UserId.ToString(), jwt.Subject);
        Assert.False(string.IsNullOrEmpty(jwt.Id));

        await using var db2 = CreatePlatformDb();
        var user = await db2.Users.FirstAsync(x => x.Id == created.UserId);
        db2.Users.Remove(user);
        var tokens = await db2.RefreshTokens.Where(t => t.UserId == created.UserId).ToListAsync();
        db2.RefreshTokens.RemoveRange(tokens);
        var audits = await db2.AuditLogs
            .Where(a => a.EntityId == created.UserId.ToString())
            .ToListAsync();
        db2.AuditLogs.RemoveRange(audits);
        await RemoveManagerAsync(db2, managerId, grantId);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_010_CreateUser_WithoutActor_WhenUsersExist_ReturnsForbidden()
    {
        // A0-3 Step 4 service-layer revalidation (SEC-10): the bootstrap
        // window is closed on a database that has users, and no actor means
        // no platform.user.manage — the create must be rejected even though
        // the endpoint policy would have been the only other gate.
        // The precondition (at least one user exists) is established here so
        // the test holds on a fresh ephemeral CI database, not only on the
        // shared dev database where users already exist.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (managerId, grantId) = await CreateManagerAsync(suffix);

        await using var db = CreatePlatformDb();
        var auth = CreateAuth(db);

        var denied = await auth.CreateUserAsync(
            $"bf_{suffix}", $"{suffix}@test.local", "Passw0rd!x", "Blocked", actingUserId: null);

        Assert.False(denied.Success);
        Assert.Equal("forbidden", denied.Error);
        Assert.Equal(Guid.Empty, denied.UserId);

        // Nothing was written: no user row for the rejected create.
        await using var db2 = CreatePlatformDb();
        Assert.False(await db2.Users.AnyAsync(x => x.UserName == $"bf_{suffix}"));

        await RemoveManagerAsync(db2, managerId, grantId);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_010_CreateUser_ConcurrentCreates_Succeed_SerializedByAppLock()
    {
        // A0-3 Step 4: sp_getapplock serializes account creation — two
        // parallel creates (both past the bootstrap window) must both succeed
        // without deadlocking or tripping the lock timeout.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (managerId, grantId) = await CreateManagerAsync(suffix);

        await using var db1 = CreatePlatformDb();
        await using var db2 = CreatePlatformDb();
        var auth1 = CreateAuth(db1);
        var auth2 = CreateAuth(db2);
        var first = auth1.CreateUserAsync(
            $"cc1_{suffix}", $"cc1_{suffix}@test.local", "Passw0rd!x", "CC1", managerId);
        var second = auth2.CreateUserAsync(
            $"cc2_{suffix}", $"cc2_{suffix}@test.local", "Passw0rd!x", "CC2", managerId);

        var results = await Task.WhenAll(first, second);

        Assert.All(results, r => Assert.True(r.Success, r.Error));

        await using var db = CreatePlatformDb();
        var created = await db.Users
            .Where(u => u.UserName == $"cc1_{suffix}" || u.UserName == $"cc2_{suffix}")
            .ToListAsync();
        Assert.Equal(2, created.Count);
        db.Users.RemoveRange(created);
        await RemoveManagerAsync(db, managerId, grantId);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// A0-3 Step 4 fixture: creates a manager user directly (TestUsers) and
    /// grants platform.user.manage through a role bundle — the actor every
    /// CreateUserAsync call past the bootstrap window needs.
    /// </summary>
    private static async Task<(Guid UserId, Guid GrantRoleId)> CreateManagerAsync(string suffix)
    {
        var managerName = $"mgr_{suffix}";
        var userId = await TestUsers.CreateAsync(managerName, "Passw0rd!x", "Manager");
        var grantRoleId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            managerName, PermissionCodes.PlatformUserManage);
        return (userId, grantRoleId);
    }

    private static async Task RemoveManagerAsync(PlatformCoreDbContext db, Guid userId, Guid grantRoleId)
    {
        await TestPermissionGrant.RemoveGrantAsync(grantRoleId);
        var manager = await db.Users.FirstAsync(x => x.Id == userId);
        db.Users.Remove(manager);
    }

    [Fact]
    public async Task PLT_011_Refresh_Rotation_Works_And_Reuse_Detects()
    {
        await using var db = CreatePlatformDb();
        var auth = CreateAuth(db);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"rf_{suffix}";

        // A0-3 Step 4: creation past the bootstrap window needs an authorized actor.
        var (managerId, grantId) = await CreateManagerAsync(suffix);
        var created = await auth.CreateUserAsync(
            userName, $"{suffix}@test.local", "Passw0rd!x", "RF User", managerId);
        Assert.True(created.Success);

        var login = await auth.LoginAsync(userName, "Passw0rd!x");
        Assert.True(login.Success);

        var refreshed = await auth.RefreshAsync(login.RefreshToken!);
        Assert.True(refreshed.Success);
        Assert.NotNull(refreshed.RefreshToken);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);

        // Refresh also issues a JWT access token (ADR-004), same family untouched.
        Assert.Equal(3, refreshed.AccessToken!.Split('.').Length);
        Assert.NotNull(refreshed.AccessTokenExpiresAt);

        // Presenting the OLD (rotated) token again must fail as reuse.
        var reuse = await auth.RefreshAsync(login.RefreshToken!);
        Assert.False(reuse.Success);
        Assert.Equal("refresh_reuse", reuse.Error);

        await using var db2 = CreatePlatformDb();
        var user = await db2.Users.FirstAsync(x => x.Id == created.UserId);
        var tokens = await db2.RefreshTokens.Where(t => t.UserId == created.UserId).ToListAsync();
        db2.RefreshTokens.RemoveRange(tokens);
        db2.Users.Remove(user);
        var audits = await db2.AuditLogs.Where(a => a.EntityId == created.UserId.ToString()).ToListAsync();
        db2.AuditLogs.RemoveRange(audits);
        var reuseAudits = await db2.AuditLogs.Where(a => a.Action == "refresh.reuse_detected").ToListAsync();
        db2.AuditLogs.RemoveRange(reuseAudits);
        await RemoveManagerAsync(db2, managerId, grantId);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_012_Lockout_AfterFiveFailures()
    {
        await using var db = CreatePlatformDb();
        var auth = CreateAuth(db);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"lk_{suffix}";
        // A0-3 Step 4: creation past the bootstrap window needs an authorized actor.
        var (managerId, grantId) = await CreateManagerAsync(suffix);
        var created = await auth.CreateUserAsync(
            userName, $"{suffix}@test.local", "Passw0rd!x", "LK", managerId);
        Assert.True(created.Success);

        for (var i = 0; i < 5; i++)
        {
            var fail = await auth.LoginAsync(userName, "wrong");
            Assert.False(fail.Success);
        }

        var locked = await auth.LoginAsync(userName, "Passw0rd!x");
        Assert.False(locked.Success);
        Assert.Equal("locked_out", locked.Error);
        Assert.NotNull(locked.LockedUntil);

        await using var db2 = CreatePlatformDb();
        var user = await db2.Users.FirstAsync(x => x.Id == created.UserId);
        var tokens = await db2.RefreshTokens.Where(t => t.UserId == created.UserId).ToListAsync();
        db2.RefreshTokens.RemoveRange(tokens);
        db2.Users.Remove(user);
        var audits = await db2.AuditLogs.Where(a => a.EntityId == created.UserId.ToString()).ToListAsync();
        db2.AuditLogs.RemoveRange(audits);
        await RemoveManagerAsync(db2, managerId, grantId);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task INT_050_ApiKey_IssueAuthenticate_Revoke_ScopeCheck()
    {
        await using var db = CreatePlatformDb();
        var audit = new PlatformSharedServices(db, TimeProvider.System);
        var service = new ApiKeyService(db, audit, TimeProvider.System);

        var issued = await service.IssueAsync(
            "test-key",
            new[] { "platform.user.read" },
            DateTimeOffset.UtcNow.AddDays(30),
            null);

        Assert.StartsWith("sfk_", issued.Secret);
        Assert.False(string.IsNullOrEmpty(issued.PublicId));

        var authed = await service.AuthenticateAsync(issued.Secret);
        Assert.NotNull(authed);
        Assert.True(ApiKeyService.HasScope(authed!, "platform.user.read"));
        Assert.False(ApiKeyService.HasScope(authed!, "platform.user.write"));

        Assert.True(await service.RevokeAsync(issued.Id));
        var afterRevoke = await service.AuthenticateAsync(issued.Secret);
        Assert.Null(afterRevoke);

        await using var db2 = CreatePlatformDb();
        var keys = await db2.ApiKeys.Where(k => k.Id == issued.Id).ToListAsync();
        db2.ApiKeys.RemoveRange(keys);
        var scopes = await db2.ApiKeyScopes.Where(s => s.ApiKeyId == issued.Id).ToListAsync();
        db2.ApiKeyScopes.RemoveRange(scopes);
        // Scope strictly to this test's key: a global "apikey." sweep raced
        // with HttpAuthMatrixTests cleanup (affected-count exceptions).
        var audits = await db2.AuditLogs
            .Where(a => a.EntityId == issued.Id.ToString())
            .ToListAsync();
        db2.AuditLogs.RemoveRange(audits);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_050_NotificationRoundTrip_WithFakeEmail()
    {
        await using var db = CreatePlatformDb();
        var store = new PlatformSharedServices(db, TimeProvider.System);
        var email = new FakeEmailSender(new EmailOptions());

        var id = await store.RaiseAsync(
            "stock.below_reorder",
            null,
            null,
            "Low stock",
            "Item A is below reorder point",
            "/inventory/stock");

        var row = await db.Notifications.FirstAsync(n => n.Id == id);
        Assert.Equal("Low stock", row.Title);

        await email.SendAsync("dev@supplyforge.local", "[SupplyForge] stock.below_reorder", row.Body);
        Assert.Single(email.Sent);

        db.Notifications.Remove(row);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_020_PermissionResolver_UnionAcrossRoles_AndDenyDefault()
    {
        // A0-3 Step 2 against real SQL: effective permissions are the union of
        // every role the user holds; a role without the code grants nothing;
        // unknown code and unknown user are denied (PLT-022 deny default).
        await using var db = CreatePlatformDb();
        var resolver = new PermissionResolver(db);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var code = $"test.union.{suffix}";

        static Role NewRole(string code) => new()
        {
            Id = Guid.NewGuid(),
            Code = code,
            Name = code,
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        static User NewUser(string name) => new()
        {
            Id = Guid.NewGuid(),
            UserName = name,
            Email = $"{name}@test.local",
            PasswordHash = "not-a-login-test",
            DisplayName = name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var permission = new Permission { Id = Guid.NewGuid(), Code = code, Name = "union test" };
        var roleWithout = NewRole($"tu1_{suffix}");
        var roleWith = NewRole($"tu2_{suffix}");
        roleWith.RolePermissions.Add(new RolePermission
        {
            RoleId = roleWith.Id,
            PermissionId = permission.Id,
        });

        var userUnion = NewUser($"tua_{suffix}");
        userUnion.UserRoles.Add(new UserRole { UserId = userUnion.Id, RoleId = roleWithout.Id });
        userUnion.UserRoles.Add(new UserRole { UserId = userUnion.Id, RoleId = roleWith.Id });

        var userSingle = NewUser($"tub_{suffix}");
        userSingle.UserRoles.Add(new UserRole { UserId = userSingle.Id, RoleId = roleWithout.Id });

        db.AddRange(permission, roleWithout, roleWith, userUnion, userSingle);
        await db.SaveChangesAsync();

        // Grant sits on ONE of userUnion's two roles → union grants it.
        Assert.True(await resolver.HasPermissionAsync(userUnion.Id, code));
        // Same roles without the code → deny.
        Assert.False(await resolver.HasPermissionAsync(userSingle.Id, code));
        // Code outside the catalogue → deny.
        Assert.False(await resolver.HasPermissionAsync(userUnion.Id, $"test.unknown.{suffix}"));
        // User that does not exist → deny.
        Assert.False(await resolver.HasPermissionAsync(Guid.NewGuid(), code));

        await using var db2 = CreatePlatformDb();
        var userIds = new[] { userUnion.Id, userSingle.Id };
        var roleIds = new[] { roleWithout.Id, roleWith.Id };
        await db2.UserRoles.Where(ur => userIds.Contains(ur.UserId)).ExecuteDeleteAsync();
        await db2.RolePermissions.Where(rp => roleIds.Contains(rp.RoleId)).ExecuteDeleteAsync();
        await db2.Roles.Where(r => roleIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db2.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        await db2.Permissions.Where(p => p.Id == permission.Id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task IDEM_01_Idempotency_Claim_MismatchDetected()
    {
        await using var db = CreatePlatformDb();
        var store = new PlatformSharedServices(db, TimeProvider.System);
        var key = $"it-idem-{Guid.NewGuid():N}";

        var first = await store.TryClaimAsync(key, "hash-a");
        Assert.Equal(IdempotencyClaimStatus.Claimed, first.Status);

        var same = await store.TryClaimAsync(key, "hash-a");
        Assert.Equal(IdempotencyClaimStatus.InProgress, same.Status);

        var mismatch = await store.TryClaimAsync(key, "hash-b");
        Assert.Equal(IdempotencyClaimStatus.PayloadMismatch, mismatch.Status);

        await store.CompleteAsync(key, "{\"ok\":true}");
        var completed = await store.TryClaimAsync(key, "hash-a");
        Assert.Equal(IdempotencyClaimStatus.AlreadyCompleted, completed.Status);

        await using var db2 = CreatePlatformDb();
        var row = await db2.IdempotencyKeys.FirstAsync(x => x.Key == key);
        db2.IdempotencyKeys.Remove(row);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task PLT_044_AuditLog_SensitiveDetails_RedactedBeforePersist()
    {
        // PLT-044 on the real write path: details go through the shared
        // audit writer, so configured sensitive fields must never reach the
        // table — regardless of which module wrote them.
        var entityId = Guid.NewGuid().ToString();
        var details = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["password"] = "PlainPw!123",
            ["apiKeySecret"] = "sfk_live_hidden9",
            ["cardNumber"] = "4111111111111234",
            ["note"] = "visible-note",
        });

        await using var db = CreatePlatformDb();
        var audit = new PlatformSharedServices(db, TimeProvider.System);
        await audit.WriteAsync("test", null, "plt044.redaction", "Test", entityId, null, details);

        var row = await db.AuditLogs.FirstAsync(a => a.EntityId == entityId);
        Assert.DoesNotContain("PlainPw!123", row.DetailsJson);
        Assert.DoesNotContain("sfk_live_hidden9", row.DetailsJson);
        Assert.DoesNotContain("4111111111111234", row.DetailsJson);
        Assert.Contains(SensitiveDataRedactor.RedactedValue, row.DetailsJson);
        Assert.Contains("1234", row.DetailsJson);
        Assert.Contains("visible-note", row.DetailsJson);

        await db.AuditLogs.Where(a => a.EntityId == entityId).ExecuteDeleteAsync();
    }
}
