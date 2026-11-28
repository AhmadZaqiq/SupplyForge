using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Security;
using SupplyForge.PlatformCore.Infrastructure.Services;
using Xunit;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// Integration tests for admin user lifecycle (MD-004, BRN-009, BRN-010, PLT-ROLE-3).
/// Tests run against the shared SQL Server dev database.
/// Requirement IDs are included in every test name (NFR-051).
/// </summary>
public sealed class UserServiceTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

    private static Guid CompanyAdminRoleId;
    private static Guid BranchManagerRoleId;
    private static Guid WarehouseManagerRoleId;

    private UserService _service = null!;
    private PlatformCoreDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;
        _db = new PlatformCoreDbContext(opts);

        // Look up seeded role ids from the catalogue
        CompanyAdminRoleId = LookupRoleId("company_administrator");
        BranchManagerRoleId = LookupRoleId("branch_manager");
        WarehouseManagerRoleId = LookupRoleId("warehouse_manager");

        // System scope for test setup (Q-E option a): seed/migration/background
        // jobs only — never from HTTP.
        var holder = new BranchScopeHolder { Scope = SystemBranchScope.Create() };
        _service = new UserService(_db, holder.Scope);
    }

    public async Task DisposeAsync()
    {
        // Clean up all users created by tests to ensure test isolation
        var users = await _db.Users.ToListAsync();
        _db.Users.RemoveRange(users);
        await _db.SaveChangesAsync();
        _db.Dispose();
        await Task.CompletedTask;
    }

    private static Guid LookupRoleId(string code)
    {
        // Synchronous lookup using a throwaway context; seeded roles must exist
        // in the dev database (PlatformCatalogueService runs at Host startup).
        using var db = new PlatformCoreDbContext(
            new DbContextOptionsBuilder<PlatformCoreDbContext>()
                .UseSqlServer(ConnectionString, sql =>
                    sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
                .Options);
        var role = db.Roles.FirstOrDefault(r => r.Code == code);
        return role?.Id ?? Guid.NewGuid();
    }

    private static (string UserName, string Email) UniqueIdentity(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        return ($"{prefix}_{suffix}", $"{prefix}_{suffix}@test.local");
    }

    [Fact]
    public async Task MD_004_CreateUser_WithRolesAndBranches()
    {
        // Act
        var (userName, email) = UniqueIdentity("alice");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Alice",
            new[] { CompanyAdminRoleId, BranchManagerRoleId },
            new[] { Guid.NewGuid(), Guid.NewGuid() });

        // Assert
        var user = await _db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.BranchScopes)
            .FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.StartsWith("alice_", user.UserName);
        Assert.True(user.IsActive);
        Assert.Equal(2, user.UserRoles.Count);
        Assert.Equal(2, user.BranchScopes.Count);
    }

    [Fact]
    public async Task MD_004_CreateUser_DuplicateUserName_Rejected()
    {
        // Arrange
        var (userName, email) = UniqueIdentity("bob");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Bob",
            new[] { CompanyAdminRoleId }, new List<Guid>());

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.CreateUserAsync(
                userName, "other@test.local", "hash", "Bob2",
                new[] { CompanyAdminRoleId }, new List<Guid>()));
    }

    [Fact]
    public async Task MD_004_AssignRoles_AddsAndRemoves()
    {
        // Arrange
        var (userName, email) = UniqueIdentity("carol");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Carol",
            new[] { CompanyAdminRoleId }, new List<Guid>());

        // Act - add BranchManager role
        await _service.AssignRolesAsync(userId, new[] { CompanyAdminRoleId, BranchManagerRoleId });

        // Assert
        var user = await _db.Users.Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.Equal(2, user.UserRoles.Count);

        // Act - remove CompanyAdministrator role
        await _service.AssignRolesAsync(userId, new[] { BranchManagerRoleId });

        // Assert - only BranchManager remains
        user = await _db.Users.Include(u => u.UserRoles)
            .FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.Single(user.UserRoles);
        Assert.Equal(BranchManagerRoleId, user.UserRoles.First().RoleId);
    }

    [Fact]
    public async Task MD_004_AssignBranches_AddsAndRemoves()
    {
        // Arrange
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var (userName, email) = UniqueIdentity("dave");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Dave",
            new[] { WarehouseManagerRoleId }, new[] { branchA });

        // Act - add branchB
        await _service.AssignBranchesAsync(userId, new[] { branchA, branchB });

        // Assert
        var user = await _db.Users.Include(u => u.BranchScopes)
            .FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.Equal(2, user.BranchScopes.Count);

        // Act - remove branchA
        await _service.AssignBranchesAsync(userId, new[] { branchB });

        // Assert - only branchB remains
        user = await _db.Users.Include(u => u.BranchScopes)
            .FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.Single(user.BranchScopes);
        Assert.Equal(branchB, user.BranchScopes.First().BranchId);
    }

    [Fact]
    public async Task MD_004_DeactivateUser_ActiveToInactive()
    {
        // Arrange
        var (userName, email) = UniqueIdentity("eve");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Eve",
            new[] { WarehouseManagerRoleId }, new List<Guid>());

        // Act
        await _service.DeactivateUserAsync(userId);

        // Assert
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.False(user.IsActive);
        Assert.NotNull(user.UpdatedAt);
    }

    [Fact]
    public async Task MD_004_ActivateUser_InactiveToActive()
    {
        // Arrange
        var (userName, email) = UniqueIdentity("frank");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Frank",
            new[] { WarehouseManagerRoleId }, new List<Guid>());
        await _service.DeactivateUserAsync(userId);

        // Act
        await _service.ActivateUserAsync(userId);

        // Assert
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task BRN_009_LastActiveCompanyAdmin_CannotBeDeactivated()
    {
        // Arrange - single active Company Administrator
        var (userName, email) = UniqueIdentity("grace");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Grace",
            new[] { CompanyAdminRoleId }, new List<Guid>());

        // Act & Assert - last active Company Admin cannot be deactivated
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeactivateUserAsync(userId));

        // Assert - user remains active
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task BRN_009_LastActiveCompanyAdmin_WithInactiveAdmin_StillBlocked()
    {
        // Arrange - one active Company Admin, one inactive Company Admin
        var (userName, email) = UniqueIdentity("heidi");
        var activeAdminId = await _service.CreateUserAsync(
            userName, email, "hash", "Heidi",
            new[] { CompanyAdminRoleId }, new List<Guid>());
        var (userName2, email2) = UniqueIdentity("ivan");
        var inactiveAdminId = await _service.CreateUserAsync(
            userName2, email2, "hash", "Ivan",
            new[] { CompanyAdminRoleId }, new List<Guid>());
        await _service.DeactivateUserAsync(inactiveAdminId);

        // Act & Assert - only one active Company Admin remains, cannot deactivate
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.DeactivateUserAsync(activeAdminId));
    }

    [Fact]
    public async Task BRN_009_LastActiveCompanyAdmin_WithAnotherActiveAdmin_CanBeDeactivated()
    {
        // Arrange - two active Company Administrators
        var (userName, email) = UniqueIdentity("judy");
        var admin1Id = await _service.CreateUserAsync(
            userName, email, "hash", "Judy",
            new[] { CompanyAdminRoleId }, new List<Guid>());
        var (userName2, email2) = UniqueIdentity("ken");
        var admin2Id = await _service.CreateUserAsync(
            userName2, email2, "hash", "Ken",
            new[] { CompanyAdminRoleId }, new List<Guid>());

        // Act - deactivating one is allowed when another active Company Admin exists
        await _service.DeactivateUserAsync(admin1Id);

        // Assert - admin1 is inactive, admin2 remains active
        var admin1 = await _db.Users.FirstOrDefaultAsync(u => u.Id == admin1Id);
        var admin2 = await _db.Users.FirstOrDefaultAsync(u => u.Id == admin2Id);
        Assert.NotNull(admin1);
        Assert.NotNull(admin2);
        Assert.False(admin1.IsActive);
        Assert.True(admin2.IsActive);
    }

    [Fact]
    public async Task BRN_009_NonAdminUser_CanAlwaysBeDeactivated()
    {
        // Arrange - non-admin user
        var (userName, email) = UniqueIdentity("leo");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Leo",
            new[] { WarehouseManagerRoleId }, new List<Guid>());

        // Act - non-admin can always be deactivated
        await _service.DeactivateUserAsync(userId);

        // Assert
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task PLT_ROLE_3_BranchScopedCaller_CannotAssignForeignBranch()
    {
        // Arrange - system scope creates a user, then we switch to a branch-scoped
        // caller that cannot access the foreign branch
        var foreignBranch = Guid.NewGuid();
        var (userName, email) = UniqueIdentity("mallory");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Mallory",
            new[] { WarehouseManagerRoleId }, new List<Guid>());

        // Act & Assert - branch-scoped caller cannot assign foreign branch
        var holder = new BranchScopeHolder
        {
            Scope = new RequestBranchScope(new[] { Guid.NewGuid() }, isCompanyWide: false)
        };
        var scopedService = new UserService(_db, holder.Scope);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scopedService.AssignBranchesAsync(userId, new[] { foreignBranch }));
    }

    [Fact]
    public async Task PLT_ROLE_3_CompanyWideCaller_CanAssignAnyBranch()
    {
        // Arrange - company-wide caller can assign any branch
        var foreignBranch = Guid.NewGuid();
        var (userName, email) = UniqueIdentity("nancy");
        var userId = await _service.CreateUserAsync(
            userName, email, "hash", "Nancy",
            new[] { WarehouseManagerRoleId }, new List<Guid>());

        // Act - company-wide scope can assign any branch
        var holder = new BranchScopeHolder { Scope = SystemBranchScope.Create() };
        var companyWideService = new UserService(_db, holder.Scope);

        await companyWideService.AssignBranchesAsync(userId, new[] { foreignBranch });

        // Assert
        var user = await _db.Users.Include(u => u.BranchScopes)
            .FirstOrDefaultAsync(u => u.Id == userId);
        Assert.NotNull(user);
        Assert.Contains(user.BranchScopes, bs => bs.BranchId == foreignBranch);
    }
}
