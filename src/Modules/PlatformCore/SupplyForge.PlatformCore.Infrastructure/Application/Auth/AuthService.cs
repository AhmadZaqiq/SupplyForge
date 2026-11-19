using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Data;

namespace SupplyForge.PlatformCore.Infrastructure.Application.Auth;

public sealed record CreateUserResult(Guid UserId, bool Success, string? Error);

public sealed record LoginResult(
    bool Success,
    string? Error,
    Guid? UserId,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? AccessTokenExpiresAt);

public sealed record RefreshResult(
    bool Success,
    string? Error,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? AccessTokenExpiresAt);

/// <summary>
/// A0 auth use cases: PLT-010 (create), PLT-011 (login/refresh), PLT-012 (lockout).
/// Access tokens are issued as JWT (ADR-004) via <see cref="IAccessTokenIssuer"/>;
/// validation is done by the standard JwtBearer handler, never here.
/// </summary>
public sealed class AuthService
{
    public const int MaxFailedLogins = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(14);

    private readonly PlatformCoreDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;
    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IPermissionResolver _permissionResolver;
    private readonly IDbSessionFactory _sessionFactory;

    private const string UserCreateLockResource = "supplyforge.user.create";
    private const int UserCreateLockTimeoutMs = 5000;

    public AuthService(
        PlatformCoreDbContext db,
        IAuditWriter audit,
        TimeProvider clock,
        IAccessTokenIssuer accessTokenIssuer,
        IPermissionResolver permissionResolver,
        IDbSessionFactory sessionFactory)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
        _accessTokenIssuer = accessTokenIssuer;
        _permissionResolver = permissionResolver;
        _sessionFactory = sessionFactory;
    }

    /// <summary>
    /// PLT-010 create-user. A0-3 Step 4: creation is serialized on the
    /// database application lock <c>supplyforge.user.create</c> and the
    /// creation invariant is revalidated inside it, closing the race between
    /// the endpoint's create-user policy check and this write (SEC-10): while
    /// no user exists any caller may bootstrap; afterwards
    /// <paramref name="actingUserId"/> must hold <c>platform.user.manage</c>,
    /// re-resolved from current data. Insert and audit entry commit atomically
    /// inside the ADR-003 ambient session (A0-6 S4) with the lock
    /// (transaction-owned lock, released on commit/rollback).
    /// </summary>
    public async Task<CreateUserResult> CreateUserAsync(
        string userName,
        string email,
        string password,
        string displayName,
        Guid? actingUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userName) ||
            string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(password))
        {
            return new CreateUserResult(Guid.Empty, false, "validation_failed");
        }

        // One ADR-003 ambient session owns lock + revalidation + insert +
        // audit (A0-6 S4): the session joins this context (and Integrations)
        // on one shared connection/transaction, so sp_getapplock below stays
        // transaction-owned and the user + audit rows commit atomically.
        // Early returns dispose without commit ⇒ rollback — the same
        // semantics the private BeginTransactionAsync had before A0-6.
        await using var session = await _sessionFactory.BeginAsync(cancellationToken);
        await AcquireUserCreateLockAsync(cancellationToken);

        var anyUser = await _db.Users.AnyAsync(cancellationToken);
        if (anyUser && !await MayCreateAsync(actingUserId, cancellationToken))
        {
            // Two concurrent first-run creates: the lock winner inserts first,
            // the loser revalidates here and is rejected (bootstrap closed).
            return new CreateUserResult(Guid.Empty, false, "forbidden");
        }

        var exists = await _db.Users.AnyAsync(
            x => x.UserName == userName || x.Email == email,
            cancellationToken);
        if (exists)
        {
            return new CreateUserResult(Guid.Empty, false, "user_exists");
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName.Trim(),
            Email = email.Trim(),
            PasswordHash = PasswordHasher.Hash(password),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? userName : displayName,
            IsActive = true,
            CreatedAt = _clock.GetUtcNow(),
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        // Same DbContext as the user insert → same session: a created
        // user can never exist without its audit entry.
        await _audit.WriteAsync(
            "user", user.Id.ToString(), "user.created", "User", user.Id.ToString(),
            null, null, cancellationToken);

        await session.CommitAsync(cancellationToken);
        return new CreateUserResult(user.Id, true, null);
    }

    /// <summary>
    /// Service-layer revalidation of the create-user invariant (SEC-10). The
    /// endpoint policy already authorized this request; re-resolving the
    /// permission here closes the gap between that check and the insert.
    /// </summary>
    private async Task<bool> MayCreateAsync(Guid? actingUserId, CancellationToken cancellationToken)
    {
        return actingUserId is { } actorId
            && actorId != Guid.Empty
            && await _permissionResolver.HasPermissionAsync(
                actorId, PermissionCodes.PlatformUserManage, cancellationToken);
    }

    /// <summary>
    /// Exclusive, transaction-owned application lock serializing account
    /// creation (A0-3 Step 4; <c>sp_getapplock</c>). Released automatically at
    /// commit/rollback — no manual release path to forget.
    /// </summary>
    private async Task AcquireUserCreateLockAsync(CancellationToken cancellationToken)
    {
        var efTransaction = _db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "sp_getapplock for user creation requires an active transaction.");

        // Raw ADO on the context's connection, enlisted in the context's
        // transaction — the lock is therefore transaction-owned.
        await using var command = _db.Database.GetDbConnection().CreateCommand();
        command.Transaction = efTransaction.GetDbTransaction();
        command.CommandText = """
            DECLARE @result int;
            EXEC @result = sp_getapplock
                @Resource = @resource,
                @LockMode = 'Exclusive',
                @LockTimeout = @timeout,
                @LockOwner = 'Transaction';
            SELECT @result;
            """;

        command.Parameters.Add(new SqlParameter("@resource", SqlDbType.NVarChar, 255)
        {
            Value = UserCreateLockResource,
        });
        command.Parameters.Add(new SqlParameter("@timeout", SqlDbType.Int)
        {
            Value = UserCreateLockTimeoutMs,
        });

        // sp_getapplock grants >= 0 (0 immediately, 1 after waiting) and
        // returns negative for timeout/cancel/deadlock — never proceed
        // without the lock (fail closed).
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not int granted || granted < 0)
        {
            throw new InvalidOperationException(
                $"sp_getapplock could not grant '{UserCreateLockResource}' (result: {result}).");
        }
    }

    public async Task<LoginResult> LoginAsync(
        string userName,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(
            x => x.UserName == userName || x.Email == userName,
            cancellationToken);

        if (user is null)
        {
            // Constant-ish work to reduce user enumeration; still fail closed.
            PasswordHasher.Verify(password ?? string.Empty, "pbkdf2$100000$AA==$AA==");
            return new LoginResult(false, "invalid_credentials", null, null, null, null, null);
        }

        var now = _clock.GetUtcNow();

        if (!user.IsActive)
        {
            return new LoginResult(false, "user_inactive", null, null, null, null, null);
        }

        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            return new LoginResult(false, "locked_out", null, null, null, lockedUntil, null);
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
        {
            user.FailedLoginCount++;
            if (user.FailedLoginCount >= MaxFailedLogins)
            {
                user.LockedUntil = now.Add(LockoutDuration);
                user.FailedLoginCount = 0;
                await _audit.WriteAsync(
                    "system", null, "user.locked", "User", user.Id.ToString(),
                    null, $"failures={MaxFailedLogins}", cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return new LoginResult(false, "invalid_credentials", null, null, null, user.LockedUntil, null);
        }

        user.FailedLoginCount = 0;
        user.LockedUntil = null;

        var (refreshToken, refreshHash) = OpaqueTokens.Create();
        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshHash,
            FamilyId = Guid.NewGuid(),
            ExpiresAt = now.Add(RefreshTokenLifetime),
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);

        var access = _accessTokenIssuer.Issue(user.Id, user.Email);

        await _audit.WriteAsync(
            "user", user.Id.ToString(), "user.login", "User", user.Id.ToString(),
            null, null, cancellationToken);

        return new LoginResult(true, null, user.Id, access.Token, refreshToken, null, access.ExpiresAt);
    }

    public async Task<RefreshResult> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var hash = OpaqueTokens.Hash(refreshToken);
        var stored = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return new RefreshResult(false, "invalid_refresh_token", null, null, null);
        }

        var now = _clock.GetUtcNow();

        // Reuse detection: revoked token presented again → revoke entire family (SEC-03).
        if (stored.RevokedAt is not null)
        {
            var family = await _db.RefreshTokens
                .Where(t => t.FamilyId == stored.FamilyId && t.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var t in family)
            {
                t.RevokedAt = now;
            }

            await _db.SaveChangesAsync(cancellationToken);
            await _audit.WriteAsync(
                "system", null, "refresh.reuse_detected", "RefreshToken", stored.Id.ToString(),
                null, $"family={stored.FamilyId}", cancellationToken);
            return new RefreshResult(false, "refresh_reuse", null, null, null);
        }

        if (stored.ExpiresAt <= now)
        {
            return new RefreshResult(false, "refresh_expired", null, null, null);
        }

        if (!stored.User.IsActive)
        {
            return new RefreshResult(false, "user_inactive", null, null, null);
        }

        // Rotate: revoke current, issue new in same family.
        var (newToken, newHash) = OpaqueTokens.Create();
        stored.RevokedAt = now;
        stored.ReplacedByTokenHash = newHash;

        _db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = stored.UserId,
            TokenHash = newHash,
            FamilyId = stored.FamilyId,
            ExpiresAt = now.Add(RefreshTokenLifetime),
            CreatedAt = now,
        });

        await _db.SaveChangesAsync(cancellationToken);

        var access = _accessTokenIssuer.Issue(stored.UserId, stored.User.Email);
        return new RefreshResult(true, null, access.Token, newToken, access.ExpiresAt);
    }
}
