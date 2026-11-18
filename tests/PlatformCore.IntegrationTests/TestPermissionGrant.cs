using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-3 Step 2 grant setup for tests: ensures the shared catalogue
/// <c>Permission</c> row (unique Code — first writer wins, losers re-query so
/// parallel test classes never collide), creates a per-test role bundle
/// carrying the code, and links it to the user. Cleanup removes only the
/// per-test role rows; shared catalogue Permission rows persist.
/// </summary>
internal static class TestPermissionGrant
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    internal static PlatformCoreDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;
        return new PlatformCoreDbContext(opts);
    }

    /// <summary>
    /// Grants <paramref name="permissionCode"/> to the named user through a
    /// fresh role bundle. Returns the role id for <see cref="RemoveGrantAsync"/>.
    /// </summary>
    internal static async Task<Guid> GrantToUserByUserNameAsync(
        string userName,
        string permissionCode)
    {
        await using var db = CreateDb();
        var user = await db.Users.FirstAsync(u => u.UserName == userName);

        var permission = await db.Permissions.FirstOrDefaultAsync(p => p.Code == permissionCode);
        if (permission is null)
        {
            var added = new Permission
            {
                Id = Guid.NewGuid(),
                Code = permissionCode,
                Name = permissionCode,
            };
            db.Permissions.Add(added);
            try
            {
                await db.SaveChangesAsync();
                permission = added;
            }
            catch (DbUpdateException)
            {
                // Unique Permission.Code race with a parallel test class:
                // re-query the row the winner committed.
                db.ChangeTracker.Clear();
                permission = await db.Permissions.FirstAsync(p => p.Code == permissionCode);
            }
        }

        var role = new Role
        {
            Id = Guid.NewGuid(),
            Code = $"tst_{Guid.NewGuid():N}",
            Name = $"test grant {permissionCode}",
            IsSystem = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        role.RolePermissions.Add(new RolePermission
        {
            RoleId = role.Id,
            PermissionId = permission.Id,
        });
        db.Roles.Add(role);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        await db.SaveChangesAsync();

        return role.Id;
    }

    internal static async Task RemoveGrantAsync(Guid roleId)
    {
        await using var db = CreateDb();
        await db.RolePermissions.Where(rp => rp.RoleId == roleId).ExecuteDeleteAsync();
        await db.UserRoles.Where(ur => ur.RoleId == roleId).ExecuteDeleteAsync();
        await db.Roles.Where(r => r.Id == roleId).ExecuteDeleteAsync();
    }
}
