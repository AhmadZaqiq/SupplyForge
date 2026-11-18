using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.Infrastructure.Application.Auth;

/// <summary>
/// PLT-020: effective permissions are the union across every role the user
/// holds. The catalogue is the <c>Permission</c> table; a code no role grants
/// (or that does not exist) resolves to false — deny default (PLT-022),
/// re-checked per request against current data (SEC-10).
/// </summary>
public sealed class PermissionResolver : IPermissionResolver
{
    private readonly PlatformCoreDbContext _db;

    public PermissionResolver(PlatformCoreDbContext db)
    {
        _db = db;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(permissionCode))
        {
            return false;
        }

        return await _db.UserRoles
            .Where(userRole => userRole.UserId == userId)
            .SelectMany(userRole => userRole.Role.RolePermissions)
            .AnyAsync(
                rolePermission => rolePermission.Permission.Code == permissionCode,
                cancellationToken);
    }
}
