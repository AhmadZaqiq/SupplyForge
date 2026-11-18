using Microsoft.AspNetCore.Authorization;

namespace SupplyForge.Host.Authorization;

/// <summary>
/// Fail-closed requirement: never succeeds for anonymous or authenticated callers.
/// Used as FallbackPolicy (no endpoint metadata) and DefaultPolicy (bare
/// RequireAuthorization / bare IAuthorizeData) so neither path can grant access
/// by accident (PLT-022, SEC-10).
/// </summary>
public sealed class FailClosedAuthorizationRequirement : IAuthorizationRequirement;

/// <summary>
/// Deliberately never calls <see cref="AuthorizationHandlerContext.Succeed"/>.
/// </summary>
public sealed class FailClosedAuthorizationHandler
    : AuthorizationHandler<FailClosedAuthorizationRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FailClosedAuthorizationRequirement requirement)
    {
        // Fail closed: do not succeed for anonymous or authenticated principals.
        return Task.CompletedTask;
    }
}
