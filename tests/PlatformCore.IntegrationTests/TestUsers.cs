using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-3 Step 4 test setup: creates a login-capable user directly in the
/// database. POST /users is not usable as a test fixture — its create-user
/// policy allows anonymous creation only while the Users table is empty
/// (bootstrap window). <see cref="EnsureAtLeastOneUserAsync"/> closes that
/// window deterministically for tests that assert the closed-window
/// behavior. The same <see cref="PasswordHasher"/> as the service keeps
/// login flows working.
/// </summary>
internal static class TestUsers
{
    /// <summary>
    /// Ensures at least one user exists so the bootstrap window is closed —
    /// required by any test asserting the non-bootstrap behavior of
    /// POST /users (401/403). Creates a guard user only when the table is
    /// empty (a no-op on the shared dev database, which always has users)
    /// and deliberately leaves it in place: deleting the last user would
    /// reopen the window for other test classes running in parallel on a
    /// fresh ephemeral database.
    /// </summary>
    internal static async Task EnsureAtLeastOneUserAsync()
    {
        await using var db = TestPermissionGrant.CreateDb();
        if (await db.Users.AnyAsync())
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N")[..8];
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            UserName = $"guard_{suffix}",
            Email = $"guard_{suffix}@test.local",
            PasswordHash = PasswordHasher.Hash("Passw0rd!x"),
            DisplayName = "Bootstrap Guard",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Inserts an active user with a real password hash; returns the id.</summary>
    internal static async Task<Guid> CreateAsync(
        string userName,
        string password,
        string? displayName = null)
    {
        await using var db = TestPermissionGrant.CreateDb();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            Email = $"{userName}@test.local",
            PasswordHash = PasswordHasher.Hash(password),
            DisplayName = displayName ?? userName,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
