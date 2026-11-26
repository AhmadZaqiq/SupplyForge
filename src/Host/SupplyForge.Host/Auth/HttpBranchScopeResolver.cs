using System.Security.Claims;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Security;
using SupplyForge.PlatformCore.Infrastructure.Services;

namespace SupplyForge.Host.Auth;

/// <summary>
/// Builds the current <see cref="ICurrentBranchScope"/> per HTTP request
/// (A1-4 Part 2). The scope comes only from the authenticated caller's
/// <c>UserBranchScope</c> rows plus the BRN-010 company-wide rule.
/// </summary>
public sealed class HttpBranchScopeResolver : IBranchScopeResolver
{
    private readonly IBranchScopeProvider _provider;

    public HttpBranchScopeResolver(IBranchScopeProvider provider)
    {
        _provider = provider;
    }

    public async Task<ICurrentBranchScope> ResolveAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return NoBranchScope.Instance;
        }

        if (!PrincipalSubject.TryGetUserId(user, out var userId))
        {
            return NoBranchScope.Instance;
        }

        return await _provider.ResolveAsync(userId, cancellationToken);
    }
}