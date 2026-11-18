using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Application.Auth;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-3 Step 5: pins permission-code case/collation behavior for PLT-020 on
/// real SQL Server. Engineering regression guard (no new requirement): codes
/// are declared as ordinal .NET constants but resolved in SQL under the
/// database's case-insensitive collation, so these tests freeze the current
/// end-to-end behavior and prove the catalogue cannot hold ambiguous
/// case-variant rows.
/// </summary>
public sealed class PermissionCodeCaseTests
{
    [Fact]
    public async Task PLT_020_PermissionResolution_CodeComparison_FollowsDatabaseCollation()
    {
        await using var db = TestPermissionGrant.CreateDb();

        // The wrong-case assertions below only hold under a case-insensitive
        // collation; fail with a clear cause if the database ever changes.
        var collation = await db.Database
            .SqlQueryRaw<string>(
                "SELECT collation_name AS [Value] FROM sys.databases WHERE name = DB_NAME()")
            .SingleAsync();
        Assert.True(
            collation.EndsWith("_CI_AS", StringComparison.Ordinal),
            $"resolution assumes a case-insensitive collation, but the database collation is '{collation}'.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var userName = $"col_{suffix}";
        var userId = await TestUsers.CreateAsync(userName, "Passw0rd!x");
        var grantId = await TestPermissionGrant.GrantToUserByUserNameAsync(
            userName, "platform.notification.read");

        try
        {
            var resolver = new PermissionResolver(db);

            // Exact code (ordinal .NET constant → SQL comparison): granted.
            Assert.True(await resolver.HasPermissionAsync(
                userId, "platform.notification.read"));
            // Wrong case: the CI collation makes SQL treat it as the same
            // code — current end-to-end behavior, pinned here on purpose.
            Assert.True(await resolver.HasPermissionAsync(
                userId, "PLATFORM.NOTIFICATION.READ"));
            // Unknown code stays fail-closed regardless of case handling.
            Assert.False(await resolver.HasPermissionAsync(
                userId, "platform.notification.unknown"));
        }
        finally
        {
            await TestPermissionGrant.RemoveGrantAsync(grantId);
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task PLT_020_PermissionCatalogue_CaseVariantCodes_CannotCoexist()
    {
        await using var db = TestPermissionGrant.CreateDb();

        // Ensure the canonical row exists (idempotent startup seed), so the
        // variant below is always a true duplicate.
        await new PlatformCatalogueSeed(db, TimeProvider.System).EnsureSeededAsync();

        // The unique Permission.Code index runs under the CI collation: a
        // wrong-case duplicate is rejected by SQL Server itself, so the
        // catalogue can never hold two rows resolution would treat alike.
        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Code = "PLATFORM.USER.MANAGE",
            Name = "case variant of a seeded code",
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // The failed insert must leave nothing behind.
        db.ChangeTracker.Clear();
        Assert.Equal(
            1,
            await db.Permissions.CountAsync(p => p.Code == PermissionCodes.PlatformUserManage));
    }

    [Fact]
    public async Task PLT_020_SeededPermissionCodes_AreAllLowercase()
    {
        await using var db = TestPermissionGrant.CreateDb();
        await new PlatformCatalogueSeed(db, TimeProvider.System).EnsureSeededAsync();

        // Seeded rows are exactly the ones with module metadata; test-created
        // catalogue rows (Module null) are excluded.
        var codes = await db.Permissions
            .Where(p => p.Module == "PlatformCore")
            .Select(p => p.Code)
            .ToListAsync();

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.Equal(code.ToLowerInvariant(), code));
    }
}
