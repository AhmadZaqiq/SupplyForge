using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Domain;

namespace SupplyForge.PlatformCore.IntegrationTests;

/// <summary>
/// A0-3 Step 4 test setup: creates a login-capable user directly in the
/// database. POST /users is no longer usable as a test fixture — its
/// create-user policy allows anonymous creation only while the Users table is
/// empty (bootstrap window), and the shared test database has users. The same
/// <see cref="PasswordHasher"/> as the service keeps login flows working.
/// </summary>
internal static class TestUsers
{
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
