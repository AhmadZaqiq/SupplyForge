using Microsoft.AspNetCore.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.Host.Authorization;

/// <summary>
/// Authorization policy names and options for SupplyForge (PLT-022, SEC-10).
/// FallbackPolicy and DefaultPolicy both fail closed. Endpoints must declare
/// an explicit perm:* policy, <see cref="CreateUserPolicy"/>, or AllowAnonymous.
/// </summary>
public static class SupplyForgeAuthorization
{
    /// <summary>POST /api/v1/users bootstrap policy (full handler lands in A0-3 Step 4).</summary>
    public const string CreateUserPolicy = "create-user";

    /// <summary>Permission policy prefix; evaluated by <see cref="PermissionPolicyProvider"/> + IPermissionResolver (A0-3 Step 2).</summary>
    public const string PermissionPolicyPrefix = "perm:";

    // Codes come from PermissionCodes (PLT-020) so every policy name matches
    // the catalogue row the resolver looks up; the startup seed
    // (PlatformCatalogueSeed) guarantees those rows exist.
    public const string NotificationReadPolicy = PermissionPolicyPrefix + PermissionCodes.PlatformNotificationRead;
    public const string NotificationRaisePolicy = PermissionPolicyPrefix + PermissionCodes.PlatformNotificationRaise;
    public const string ApiKeyManagePolicy = PermissionPolicyPrefix + PermissionCodes.PlatformApiKeyManage;

    /// <summary>
    /// Configures fail-closed FallbackPolicy + DefaultPolicy and named policies.
    /// Extracted so unit tests can evaluate the same options the Host uses.
    /// </summary>
    public static void Configure(AuthorizationOptions options)
    {
        // No endpoint authorization metadata → deny (anonymous and authenticated).
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .AddRequirements(new FailClosedAuthorizationRequirement())
            .Build();

        // Bare RequireAuthorization() / bare IAuthorizeData selects DefaultPolicy → deny.
        options.DefaultPolicy = new AuthorizationPolicyBuilder()
            .AddRequirements(new FailClosedAuthorizationRequirement())
            .Build();

        // A0-3 Step 4: create-user gate (replaces the temporary always-true
        // policy). The handler allows anonymous creation only while the Users
        // table is empty (bootstrap window); afterwards it requires an
        // authenticated caller with platform.user.manage. No
        // RequireAuthenticatedUser on purpose: the bootstrap window must let
        // anonymous through, and failure still surfaces as 401/403 (SEC-13).
        options.AddPolicy(CreateUserPolicy, policy =>
            policy.AddRequirements(new CreateUserAuthorizationRequirement()));

        // perm:* policies are deliberately NOT registered here. They are built
        // on demand by PermissionPolicyProvider from the perm: prefix, so every
        // current or future catalogue code carries PermissionRequirement and is
        // evaluated through IPermissionResolver. A pre-registered policy would
        // be returned instead of the provider's and could skip the permission
        // requirement entirely (fail open).
    }

    public static IServiceCollection AddSupplyForgeAuthorization(this IServiceCollection services)
    {
        // Register before AddAuthorization: its TryAdd keeps this provider and
        // does not re-add DefaultAuthorizationPolicyProvider for perm:* names.
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();

        services.AddSingleton<IAuthorizationHandler, FailClosedAuthorizationHandler>();

        // Scoped: depends on IUserAccountState + IPermissionResolver → scoped
        // DbContext. The transient IAuthorizationHandlerProvider resolves
        // handlers per scope.
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, CreateUserAuthorizationHandler>();

        services.AddAuthorization(Configure);
        return services;
    }
}
