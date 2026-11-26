using SupplyForge.Host.Auth;
using SupplyForge.PlatformCore.Infrastructure.Security;

namespace SupplyForge.Host.Middleware;

/// <summary>
/// Per-request branch-scope middleware (A1-4 Part 2). Resolves the caller's
/// scope from their <c>UserBranchScope</c> rows and the BRN-010 company-wide
/// rule, then sets <see cref="BranchScopeHolder.Scope"/> for the rest of the
/// request. Unauthenticated requests and callers with no rows fail closed
/// (Q-E); the system scope is never produced here.
/// </summary>
public sealed class BranchScopeMiddleware
{
    private readonly RequestDelegate _next;

    public BranchScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IBranchScopeResolver resolver, BranchScopeHolder holder)
    {
        ArgumentNullException.ThrowIfNull(holder);
        var scope = await resolver.ResolveAsync(context.User, context.RequestAborted);
        holder.Scope = scope;
        await _next(context);
    }
}
