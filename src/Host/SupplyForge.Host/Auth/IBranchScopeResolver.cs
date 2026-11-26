using System.Security.Claims;
using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.Host.Auth;

/// <summary>
/// Resolves the current <see cref="ICurrentBranchScope"/> for an HTTP request
/// (A1-4 Part 2). Implemented by <see cref="HttpBranchScopeResolver"/> so the
/// middleware can depend on an abstraction and be unit-tested with DefaultHttpContext.
/// </summary>
public interface IBranchScopeResolver
{
    /// <summary>
    /// Builds the request scope from the authenticated caller. Never returns the
    /// system scope — that is created only by <see cref="SupplyForge.PlatformCore.Infrastructure.Security.SystemBranchScope"/>
    /// for seed, migration and background jobs (Q-E).
    /// </summary>
    Task<ICurrentBranchScope> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);
}
