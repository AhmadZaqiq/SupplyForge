using Microsoft.EntityFrameworkCore;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-3 Step 3 acceptance on real SQL Server: the runtime seed persists the
/// PLT-020 permission catalogue and the PLT-ROLE-1 system role bundles, covers
/// every Host perm:* policy code, is idempotent across restarts, and survives
/// concurrent Host starts (first writer wins on the unique Code indexes).
/// </summary>
public sealed class CatalogueSeedTests
{
    private static PlatformCatalogueSeed CreateSeed() =>
        new(TestPermissionGrant.CreateDb(), TimeProvider.System);

    [Fact]
    public async Task PLT_020_Seed_ContainsACatalogueRowForEveryHostPermissionPolicy()
    {
        await CreateSeed().EnsureSeededAsync();

        await using var db = TestPermissionGrant.CreateDb();

        // Every perm:* policy the Host declares must resolve to a catalogue
        // row, plus platform.user.manage which Step 4's create-user policy
        // will require. Without these rows the resolver fails closed and the
        // corresponding endpoints would be unreachable.
        var expectedCodes = new[]
        {
            SupplyForgeAuthorization.NotificationReadPolicy,
            SupplyForgeAuthorization.NotificationRaisePolicy,
            SupplyForgeAuthorization.ApiKeyManagePolicy,
        }
            .Select(policy => policy[SupplyForgeAuthorization.PermissionPolicyPrefix.Length..])
            .Append(PermissionCodes.PlatformUserManage)
            .Distinct()
            .ToList();

        Assert.Equal(4, expectedCodes.Count);

        foreach (var code in expectedCodes)
        {
            var permission = await db.Permissions.SingleOrDefaultAsync(p => p.Code == code);
            Assert.True(permission is not null, $"Permission catalogue row missing for '{code}'.");
            Assert.Equal("PlatformCore", permission!.Module);
        }
    }

    [Fact]
    public async Task PLT_ROLE_1_Seed_SystemRoleBundles_AreIdempotentAcrossRestarts()
    {
        // Two separate DbContexts = two Host starts; the second must be a no-op.
        await CreateSeed().EnsureSeededAsync();
        await CreateSeed().EnsureSeededAsync();

        await using var db = TestPermissionGrant.CreateDb();

        var expectedRoleCodes = new[]
        {
            SystemRoleCodes.CompanyExecutive,
            SystemRoleCodes.BranchManager,
            SystemRoleCodes.ProcurementManager,
            SystemRoleCodes.ProcurementOfficer,
            SystemRoleCodes.WarehouseManager,
            SystemRoleCodes.WarehouseOperator,
            SystemRoleCodes.QualityInspector,
            SystemRoleCodes.SalesRepresentative,
            SystemRoleCodes.Accountant,
            SystemRoleCodes.CompanyAdministrator,
            SystemRoleCodes.IntegrationClient,
        };

        var roles = await db.Roles
            .Where(r => expectedRoleCodes.Contains(r.Code))
            .ToListAsync();

        // Unique Role.Code index: duplicates are impossible, so an exact set
        // match proves every system role exists exactly once.
        Assert.Equal(expectedRoleCodes.Length, roles.Count);
        Assert.All(roles, role => Assert.True(role.IsSystem, $"'{role.Code}' must be a system role."));

        // Only PRD-backed bundle mapping (02b §2.6.1): Company Administrator
        // manages users. Everything else stays unmapped until module
        // permission matrices (PRD §6.4.1) define it.
        var adminRole = roles.Single(r => r.Code == SystemRoleCodes.CompanyAdministrator);
        var grantedCodes = await db.RolePermissions
            .Where(rp => rp.RoleId == adminRole.Id)
            .Select(rp => rp.Permission.Code)
            .ToListAsync();
        Assert.Equal(new[] { PermissionCodes.PlatformUserManage }, grantedCodes);
    }

    [Fact]
    public async Task PLT_020_Seed_ConcurrentStartup_EachCodeIsSeededExactlyOnce()
    {
        // Two Host instances starting against the same database: both run the
        // full seed concurrently; the loser of each insert race re-queries the
        // winner's row instead of failing startup.
        await Task.WhenAll(
            CreateSeed().EnsureSeededAsync(),
            CreateSeed().EnsureSeededAsync());

        await using var db = TestPermissionGrant.CreateDb();

        var permissionRows = await db.Permissions
            .CountAsync(p => p.Code == PermissionCodes.PlatformUserManage);
        Assert.Equal(1, permissionRows);

        var roleRows = await db.Roles
            .CountAsync(r => r.Code == SystemRoleCodes.CompanyAdministrator);
        Assert.Equal(1, roleRows);
    }
}
