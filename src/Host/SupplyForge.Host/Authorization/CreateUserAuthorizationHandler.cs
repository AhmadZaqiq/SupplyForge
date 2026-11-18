using Microsoft.AspNetCore.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.Host.Authorization;

/// <summary>
/// Requirement for the named <c>create-user</c> policy (PLT-022 endpoint
/// metadata; evaluated per request — SEC-10).
/// </summary>
public sealed class CreateUserAuthorizationRequirement : IAuthorizationRequirement;

/// <summary>
/// A0-3 Step 4: replaces the temporary always-true create-user policy.
/// Anonymous creation succeeds only while no user account exists (first-run
/// bootstrap window, PLT-010). Once any account exists the caller must be
/// authenticated with a resolvable subject and hold
/// <c>platform.user.manage</c> (PLT-020 catalogue). Everything else fails
/// closed: anonymous surfaces as 401, authenticated-without-permission as 403
/// (SEC-13). The service layer revalidates the same condition under
/// <c>sp_getapplock</c> before inserting (race window between this check and
/// the write).
/// </summary>
public sealed class CreateUserAuthorizationHandler
    : AuthorizationHandler<CreateUserAuthorizationRequirement>
{
    private readonly IUserAccountState _userAccountState;
    private readonly IPermissionResolver _permissionResolver;

    public CreateUserAuthorizationHandler(
        IUserAccountState userAccountState,
        IPermissionResolver permissionResolver)
    {
        _userAccountState = userAccountState;
        _permissionResolver = permissionResolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CreateUserAuthorizationRequirement requirement)
    {
        if (!await _userAccountState.HasAnyUserAsync())
        {
            // Bootstrap window open: no accounts exist, first-run creation is
            // allowed for any principal (the service rechecks under the lock).
            context.Succeed(requirement);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return; // anonymous → 401 challenge (SEC-13).
        }

        if (!PrincipalSubject.TryGetUserId(context.User, out var userId))
        {
            return; // authenticated but unresolvable subject → deny (fail closed).
        }

        if (await _permissionResolver.HasPermissionAsync(
                userId, PermissionCodes.PlatformUserManage))
        {
            context.Succeed(requirement);
        }
        // authenticated without platform.user.manage → not succeeded → 403 (SEC-13).
    }
}
