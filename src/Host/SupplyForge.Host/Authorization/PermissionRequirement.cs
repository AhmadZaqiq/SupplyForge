using Microsoft.AspNetCore.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.Host.Authorization;

/// <summary>
/// Requires the authenticated caller to hold the given permission code
/// (<c>perm:&lt;code&gt;</c> policies — PLT-022 endpoint metadata, PLT-020
/// catalogue). The code is checked server-side per request
/// (<see cref="IPermissionResolver"/>), never taken from the token (ADR-004).
/// </summary>
public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permissionCode)
    {
        PermissionCode = permissionCode;
    }

    public string PermissionCode { get; }
}

/// <summary>
/// Succeeds only when the principal is authenticated, carries a user-id claim,
/// and <see cref="IPermissionResolver"/> confirms the permission. Anonymous
/// callers stay on the 401 challenge; authenticated callers without the
/// permission are not succeeded and surface as 403 (SEC-13, ADR-004 §6).
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionResolver _permissionResolver;

    public PermissionAuthorizationHandler(IPermissionResolver permissionResolver)
    {
        _permissionResolver = permissionResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        if (!PrincipalSubject.TryGetUserId(context.User, out var userId))
        {
            // Authenticated but no resolvable subject → deny (fail closed).
            return;
        }

        var granted = await _permissionResolver.HasPermissionAsync(
            userId, requirement.PermissionCode);
        if (granted)
        {
            context.Succeed(requirement);
        }
    }
}
