using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Domain;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// BRN-011…014 branch isolation foundation for A0.
/// UserBranchScope is Platform-owned; Master Data owns Branch entities later (A1).
/// </summary>
public class BranchIsolationTests : IAsyncLifetime
{
    private const string ConnectionString =
        "Server=localhost,1433;Database=SupplyForge;User Id=sa;Password=SupplyForge_Dev_2026!;TrustServerCertificate=True;Encrypt=True";

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
        if (await db.Database.CanConnectAsync())
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

    private static PlatformCoreDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<PlatformCoreDbContext>()
            .UseSqlServer(ConnectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema))
            .Options;
        return new PlatformCoreDbContext(opts);
    }

    [Fact]
    public async Task BRN_011_UserBranchScope_StoresScopedBranches()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();

        db.Users.Add(new User
        {
            Id = userId,
            UserName = $"brn_{userId:N}"[..20],
            Email = $"{userId:N}@test.local",
            PasswordHash = "x",
            DisplayName = "BRN User",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.UserBranchScopes.AddRange(
            new UserBranchScope { Id = Guid.NewGuid(), UserId = userId, BranchId = branchA, CreatedAt = DateTimeOffset.UtcNow },
            new UserBranchScope { Id = Guid.NewGuid(), UserId = userId, BranchId = branchB, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        await using var db2 = CreateDb();
        var scopes = await db2.UserBranchScopes
            .Where(x => x.UserId == userId)
            .Select(x => x.BranchId)
            .ToListAsync();

        Assert.Equal(2, scopes.Count);
        Assert.Contains(branchA, scopes);
        Assert.Contains(branchB, scopes);

        // Isolation: another user's scopes are not visible under this user's filter.
        var otherUser = Guid.NewGuid();
        var otherScopes = await db2.UserBranchScopes
            .Where(x => x.UserId == otherUser)
            .ToListAsync();
        Assert.Empty(otherScopes);

        db2.UserBranchScopes.RemoveRange(db2.UserBranchScopes.Where(x => x.UserId == userId));
        var user = await db2.Users.FirstAsync(x => x.Id == userId);
        db2.Users.Remove(user);
        await db2.SaveChangesAsync();
    }

    [Fact]
    public async Task BRN_012_UserBranchScope_UniquePerUserBranch()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        db.Users.Add(new User
        {
            Id = userId,
            UserName = $"brn2_{userId:N}"[..20],
            Email = $"{userId:N}@test.local",
            PasswordHash = "x",
            DisplayName = "BRN2",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        db.UserBranchScopes.Add(new UserBranchScope
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BranchId = branchId,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        // Duplicate (UserId, BranchId) must violate unique index.
        db.UserBranchScopes.Add(new UserBranchScope
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BranchId = branchId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var db2 = CreateDb();
        db2.UserBranchScopes.RemoveRange(db2.UserBranchScopes.Where(x => x.UserId == userId));
        var user = await db2.Users.FirstAsync(x => x.Id == userId);
        db2.Users.Remove(user);
        await db2.SaveChangesAsync();
    }
}
