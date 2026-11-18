using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.Shared.Auditing;

namespace SupplyForge.PlatformCore.Infrastructure.Application.Integrations;

/// <summary>
/// INT-050: API key issue/revoke with permission allow-list scopes (ADR-009).
/// Header: X-Api-Key: &lt;secret&gt; (frozen). Store hash of secret only.
/// </summary>
public sealed class ApiKeyService
{
    private readonly PlatformCoreDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly TimeProvider _clock;

    public ApiKeyService(PlatformCoreDbContext db, IAuditWriter audit, TimeProvider clock)
    {
        _db = db;
        _audit = audit;
        _clock = clock;
    }

    public sealed record IssuedApiKey(Guid Id, string PublicId, string Secret);

    public async Task<IssuedApiKey> IssueAsync(
        string name,
        IEnumerable<string> permissionCodes,
        DateTimeOffset? expiresAt,
        Guid? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var publicId = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(8));
        var secret = "sfk_" + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        var key = new ApiKey
        {
            Id = Guid.NewGuid(),
            PublicId = publicId,
            SecretHash = OpaqueTokens.Hash(secret),
            Name = name,
            CreatedAt = _clock.GetUtcNow(),
            ExpiresAt = expiresAt,
            CreatedByUserId = createdByUserId,
        };

        foreach (var code in permissionCodes.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            key.Scopes.Add(new ApiKeyScope
            {
                Id = Guid.NewGuid(),
                PermissionCode = code,
            });
        }

        _db.ApiKeys.Add(key);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "system", createdByUserId?.ToString(), "apikey.issued", "ApiKey", key.Id.ToString(),
            null, $"name={name}", cancellationToken);

        return new IssuedApiKey(key.Id, publicId, secret);
    }

    public async Task<bool> RevokeAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        var key = await _db.ApiKeys.FirstOrDefaultAsync(x => x.Id == keyId, cancellationToken);
        if (key is null)
        {
            return false;
        }

        key.RevokedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(
            "system", null, "apikey.revoked", "ApiKey", key.Id.ToString(),
            null, null, cancellationToken);
        return true;
    }

    public async Task<ApiKey?> AuthenticateAsync(string secret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var hash = OpaqueTokens.Hash(secret);
        var key = await _db.ApiKeys
            .Include(k => k.Scopes)
            .FirstOrDefaultAsync(k => k.SecretHash == hash, cancellationToken);

        if (key is null || !key.IsActive)
        {
            return null;
        }

        key.LastUsedAt = _clock.GetUtcNow();
        await _db.SaveChangesAsync(cancellationToken);
        return key;
    }

    public static bool HasScope(ApiKey key, string permissionCode) =>
        key.Scopes.Any(s => string.Equals(s.PermissionCode, permissionCode, StringComparison.OrdinalIgnoreCase));
}
