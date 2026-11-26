using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SupplyForge.Host.Auth;
using SupplyForge.Host.Middleware;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A1-4 Part 2: branch-scope middleware sets the scoped holder per request and
/// never produces the system scope from an HTTP request (Q-E option a).
/// </summary>
public sealed class BranchScopeMiddlewareTests
{
    private sealed class StubResolver(ICurrentBranchScope scope) : IBranchScopeResolver
    {
        public Task<ICurrentBranchScope> ResolveAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default) =>
            Task.FromResult(scope);
    }

    [Fact]
    public async Task Q_E_Middleware_SetsHolderPerRequest_NeverSystemScope()
    {
        // Default holder is fail-closed (NoBranchScope), not the system scope.
        var defaultHolder = new BranchScopeHolder();
        Assert.Same(NoBranchScope.Instance, defaultHolder.Scope);
        Assert.False(defaultHolder.Scope.IsSystem);

        var requestScope = new RequestBranchScope(new[] { Guid.NewGuid() }, isCompanyWide: false);

        // Unauthenticated request -> fail-closed NoBranchScope, never system.
        var holder = new BranchScopeHolder();
        var context = new DefaultHttpContext();
        var middleware = new BranchScopeMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, new StubResolver(NoBranchScope.Instance), holder);

        Assert.Same(NoBranchScope.Instance, holder.Scope);
        Assert.False(holder.Scope.IsSystem);
        Assert.True(holder.Scope.HasNoBranchAccess);

        // Resolved request scope -> holder gets it, still never the system scope.
        holder = new BranchScopeHolder();
        context = new DefaultHttpContext();
        await middleware.InvokeAsync(context, new StubResolver(requestScope), holder);

        Assert.Same(requestScope, holder.Scope);
        Assert.False(holder.Scope.IsSystem);
        Assert.False(holder.Scope.HasNoBranchAccess);
    }
}
