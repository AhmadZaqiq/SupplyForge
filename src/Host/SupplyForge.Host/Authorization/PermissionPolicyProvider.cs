using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SupplyForge.Host.Authorization;

/// <summary>
/// Builds <c>perm:&lt;code&gt;</c> policies on demand (PLT-022): authenticated
/// caller + <see cref="PermissionRequirement"/> for the code embedded in the
/// policy name. Any catalogue code works without pre-registration, and every
/// perm:* name goes through the same evaluation path — registering perm:*
/// policies in <see cref="AuthorizationOptions"/> instead would bypass this
/// provider and silently skip <see cref="PermissionRequirement"/> for any code
/// not explicitly registered. Non-perm names (create-user, Fallback/Default)
/// delegate to <see cref="DefaultAuthorizationPolicyProvider"/>.
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        : base(options)
    {
    }

    public override Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName is not null &&
            policyName.StartsWith(SupplyForgeAuthorization.PermissionPolicyPrefix, StringComparison.Ordinal))
        {
            var permissionCode = policyName[SupplyForgeAuthorization.PermissionPolicyPrefix.Length..];
            if (permissionCode.Length > 0)
            {
                var policy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .AddRequirements(new PermissionRequirement(permissionCode))
                    .Build();
                return Task.FromResult<AuthorizationPolicy?>(policy);
            }
        }

        // Interface contract: policyName is non-null; the `is not null` guard
        // above narrows this branch to maybe-null, so reassert it for the base
        // call (a runtime null would fail in the base exactly as before).
        return base.GetPolicyAsync(policyName!);
    }
}
