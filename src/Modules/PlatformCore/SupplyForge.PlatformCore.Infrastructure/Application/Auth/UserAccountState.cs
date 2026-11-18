using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Infrastructure.Data;

namespace SupplyForge.PlatformCore.Infrastructure.Application.Auth;

/// <summary>
/// <see cref="IUserAccountState"/> over the Platform Core user table
/// (A0-3 Step 4). Existence check only — no projection, no tracking.
/// </summary>
public sealed class UserAccountState : IUserAccountState
{
    private readonly PlatformCoreDbContext _db;

    public UserAccountState(PlatformCoreDbContext db)
    {
        _db = db;
    }

    public Task<bool> HasAnyUserAsync(CancellationToken cancellationToken = default) =>
        _db.Users.AnyAsync(cancellationToken);
}
