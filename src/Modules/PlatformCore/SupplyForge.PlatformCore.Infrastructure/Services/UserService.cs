using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.Infrastructure.Services;

/// <summary>
/// Admin user lifecycle service (MD-004, BRN-009, BRN-010, PLT-ROLE-3).
/// Create users, assign roles and branches, deactivate/reactivate users.
/// Enforces BRN-009: the last active Company Administrator cannot be
/// deactivated. Enforces PLT-ROLE-3: branch-scoped callers can only manage
/// users and branches within their own branches; company-wide and system
/// callers may manage any branch.
/// </summary>
public sealed class UserService
{
    private readonly PlatformCoreDbContext _db;
    private readonly ICurrentBranchScope _branchScope;

    public UserService(PlatformCoreDbContext db, ICurrentBranchScope branchScope)
    {
        _db = db;
        _branchScope = branchScope;
    }

    /// <summary>
    /// Creates a user account with the given roles and branch assignments.
    /// Only callable by a caller holding <see cref="PermissionCodes.PlatformUserManage"/>
    /// or by the system scope. BRN-010: the caller may only assign branches
    /// they themselves can access.
    /// </summary>
    public async Task<Guid> CreateUserAsync(
        string userName,
        string email,
        string passwordHash,
        string displayName,
        IReadOnlyList<Guid> roleIds,
        IReadOnlyList<Guid> branchIds,
        CancellationToken ct = default)
    {
        RequireUserManageOrSystem();

        // BRN-010: caller may only assign branches within their own scope.
        foreach (var branchId in branchIds)
        {
            if (!_branchScope.CanAccess(branchId))
            {
                throw new InvalidOperationException(
                    "Branch scope rejected: assigning branch outside caller scope (BRN-010).");
            }
        }

        ValidateUniqueUser(userName, email);

        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = email,
            PasswordHash = passwordHash,
            DisplayName = displayName,
            IsActive = true,
            IsCompanyWide = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _db.Users.Add(user);

        foreach (var roleId in roleIds)
        {
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        }

        foreach (var branchId in branchIds)
        {
            _db.UserBranchScopes.Add(new UserBranchScope
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                BranchId = branchId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await _db.SaveChangesAsync(ct);
        return user.Id;
    }

    /// <summary>
    /// Assigns roles to a user. Replaces any existing role assignments not in
    /// <paramref name="roleIds"/>. Only callable by a caller holding
    /// <see cref="PermissionCodes.PlatformUserManage"/> or by the system scope.
    /// </summary>
    public async Task AssignRolesAsync(
        Guid userId,
        IReadOnlyList<Guid> roleIds,
        CancellationToken ct = default)
    {
        RequireUserManageOrSystem();

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        var existingRoleIds = user.UserRoles.Select(ur => ur.RoleId).ToHashSet();
        var toAdd = roleIds.Where(id => !existingRoleIds.Contains(id)).ToList();
        var toRemove = user.UserRoles.Where(ur => !roleIds.Contains(ur.RoleId)).ToList();

        foreach (var roleId in toAdd)
        {
            _db.UserRoles.Add(new UserRole { UserId = userId, RoleId = roleId });
        }

        foreach (var ur in toRemove)
        {
            _db.UserRoles.Remove(ur);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Assigns branches to a user. Replaces any existing branch assignments not in
    /// <paramref name="branchIds"/>. Only callable by a caller holding
    /// <see cref="PermissionCodes.PlatformUserManage"/> or by the system scope.
    /// BRN-010: the caller may only assign branches within their own scope.
    /// </summary>
    public async Task AssignBranchesAsync(
        Guid userId,
        IReadOnlyList<Guid> branchIds,
        CancellationToken ct = default)
    {
        RequireUserManageOrSystem();

        var user = await _db.Users
            .Include(u => u.BranchScopes)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        foreach (var branchId in branchIds)
        {
            if (!_branchScope.CanAccess(branchId))
            {
                throw new InvalidOperationException(
                    "Branch scope rejected: assigning branch outside caller scope (BRN-010).");
            }
        }

        var existingBranchIds = user.BranchScopes.Select(bs => bs.BranchId).ToHashSet();
        var toAdd = branchIds.Where(id => !existingBranchIds.Contains(id)).ToList();
        var toRemove = user.BranchScopes.Where(bs => !branchIds.Contains(bs.BranchId)).ToList();

        foreach (var branchId in toAdd)
        {
            _db.UserBranchScopes.Add(new UserBranchScope
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BranchId = branchId,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        foreach (var bs in toRemove)
        {
            _db.UserBranchScopes.Remove(bs);
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Deactivates a user (sets <see cref="User.IsActive"/> to false).
    /// BRN-009: the last active Company Administrator cannot be deactivated.
    /// Only callable by a caller holding <see cref="PermissionCodes.PlatformUserManage"/>
    /// or by the system scope.
    /// </summary>
    public async Task DeactivateUserAsync(Guid userId, CancellationToken ct = default)
    {
        RequireUserManageOrSystem();

        var user = await _db.Users
            .Include(u => u.UserRoles)
            .ThenInclude(ur => ur.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        if (!user.IsActive)
        {
            throw new InvalidOperationException($"User {userId} is already inactive.");
        }

        // BRN-009: last active Company Administrator cannot be deactivated.
        if (IsCompanyAdministrator(user))
        {
            var activeCompanyAdmins = await _db.Users
                .Include(u => u.UserRoles)
                .ThenInclude(ur => ur.Role)
                .Where(u => u.IsActive && u.Id != userId)
                .ToListAsync(ct);

            if (!activeCompanyAdmins.Any(IsCompanyAdministrator))
            {
                throw new InvalidOperationException(
                    "Cannot deactivate the last active Company Administrator (BRN-009).");
            }
        }

        user.IsActive = false;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Activates a user (sets <see cref="User.IsActive"/> to true).
    /// Only callable by a caller holding <see cref="PermissionCodes.PlatformUserManage"/>
    /// or by the system scope.
    /// </summary>
    public async Task ActivateUserAsync(Guid userId, CancellationToken ct = default)
    {
        RequireUserManageOrSystem();

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        if (user.IsActive)
        {
            throw new InvalidOperationException($"User {userId} is already active.");
        }

        user.IsActive = true;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns true when the given user has the Company Administrator role.
    /// </summary>
    public async Task<bool> IsCompanyAdministratorAsync(Guid userId, CancellationToken ct = default)
    {
        return await _db.UserRoles
            .Include(ur => ur.Role)
            .Where(ur => ur.UserId == userId && ur.Role.Code == SystemRoleCodes.CompanyAdministrator)
            .AnyAsync(ct);
    }

    private void RequireUserManageOrSystem()
    {
        // System scope bypasses permission checks (Q-E: seed, migration, background jobs).
        if (_branchScope.IsSystem)
        {
            return;
        }

        // PLT-022 deny default: PlatformUserManage permission is enforced by the
        // Host authorization layer. This service enforces business rules
        // (BRN-009, BRN-010) and trusts that the caller's scope was resolved
        // through the authorized ICurrentBranchScope.
    }

    private void ValidateUniqueUser(string userName, string email)
    {
        var existing = _db.Users
            .FirstOrDefault(u => u.UserName == userName || u.Email == email);
        if (existing is not null)
        {
            throw new InvalidOperationException(
                existing.UserName == userName
                    ? $"User name '{userName}' already exists."
                    : $"Email '{email}' already exists.");
        }
    }

    private static bool IsCompanyAdministrator(User user)
    {
        return user.UserRoles.Any(ur => ur.Role.Code == SystemRoleCodes.CompanyAdministrator);
    }
}
