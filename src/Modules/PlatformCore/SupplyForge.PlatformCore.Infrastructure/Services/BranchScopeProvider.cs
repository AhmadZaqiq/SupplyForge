using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.PlatformCore.Infrastructure.Services;

/// <summary>
/// Resolves the current <see cref="ICurrentBranchScope"/> from the caller's
/// <c>UserBranchScope</c> rows plus the BRN-010 company-wide rule (A1-4 Part 2,
/// Q-C). A user with no rows and no company-wide flag gets the fail-closed
/// <see cref="NoBranchScope"/> (Q-E). The system scope is never produced here.
/// </summary>
public sealed class BranchScopeProvider : IBranchScopeProvider
{
    private readonly PlatformCore.Infrastructure.Data.PlatformCoreDbContext _db;

    public BranchScopeProvider(PlatformCore.Infrastructure.Data.PlatformCoreDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Builds the scope for <paramref name="userId"/>.
    /// </summary>
    public async Task<ICurrentBranchScope> ResolveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Q-C: company-wide flag lives on platform.User (IsCompanyWide).
        var user = await _db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsCompanyWide })
            .FirstOrDefaultAsync(cancellationToken);

        var isCompanyWide = user?.IsCompanyWide ?? false;

        var branchIds = await _db.UserBranchScopes
            .Where(s => s.UserId == userId)
            .Select(s => s.BranchId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (isCompanyWide)
        {
            return new RequestBranchScope(Array.Empty<Guid>(), isCompanyWide: true);
        }

        return new RequestBranchScope(branchIds, isCompanyWide: false);
    }
}