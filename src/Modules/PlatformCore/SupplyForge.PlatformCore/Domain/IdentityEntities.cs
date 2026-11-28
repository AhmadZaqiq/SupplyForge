namespace SupplyForge.PlatformCore.Domain;

/// <summary>Platform Core domain entities — M0 skeleton (PLT-010+).</summary>
public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>BRN-010: company-wide scope flag. User assigned one+ branches or company-wide.</summary>
    public bool IsCompanyWide { get; set; }
    public int FailedLoginCount { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<UserBranchScope> BranchScopes { get; set; } = new List<UserBranchScope>();
}

public class Role
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class Permission
{
    public Guid Id { get; set; }
    /// <summary>Stable permission code from PLT-020 catalogue.</summary>
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Module { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}

public class UserRole
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

public class RolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}

public class UserBranchScope
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Branch id (Master Data owns Branch entity; A0 stores opaque id only).</summary>
    public Guid BranchId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public User User { get; set; } = null!;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    /// <summary>Opaque token; store hash only (SEC-03, ADR-004).</summary>
    public string TokenHash { get; set; } = string.Empty;
    /// <summary>Reuse-detection family id.</summary>
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive => RevokedAt is null && ExpiresAt > DateTimeOffset.UtcNow;
}

public class ApiKey
{
    public Guid Id { get; set; }
    /// <summary>Public id used in lookup (not the secret).</summary>
    public string PublicId { get; set; } = string.Empty;
    public string SecretHash { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }

    public ICollection<ApiKeyScope> Scopes { get; set; } = new List<ApiKeyScope>();

    public bool IsActive => RevokedAt is null && (ExpiresAt is null || ExpiresAt > DateTimeOffset.UtcNow);
}

public class ApiKeyScope
{
    public Guid Id { get; set; }
    public Guid ApiKeyId { get; set; }
    /// <summary>Permission code allow-list (ADR-009).</summary>
    public string PermissionCode { get; set; } = string.Empty;
    public ApiKey ApiKey { get; set; } = null!;
}
