using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A0-3 Step 2 (PLT-020, PLT-022, SEC-10): perm:* policies are built by
/// PermissionPolicyProvider and evaluated through IPermissionResolver —
/// the caller must be authenticated AND hold the permission code; everyone
/// else is denied.
/// </summary>
public sealed class PermissionAuthorizationTests
{
    [Fact]
    public async Task PLT_022_PermPolicy_Provider_BuildsPolicyWithPermissionRequirement()
    {
        var (services, policyProvider, _) = Build(DenyAll());

        var policy = await policyProvider.GetPolicyAsync(
            SupplyForgeAuthorization.NotificationReadPolicy);

        Assert.NotNull(policy);
        var requirement = Assert.Single(policy!.Requirements.OfType<PermissionRequirement>());
        Assert.Equal("platform.notification.read", requirement.PermissionCode);
        // RequireAuthenticatedUser must be part of the built policy.
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);

        await DisposeAsync(services);
    }

    [Fact]
    public async Task PLT_022_NonPermPolicyName_FallsBackToOptionsWithoutPermissionRequirement()
    {
        var (services, policyProvider, _) = Build(DenyAll());

        var policy = await policyProvider.GetPolicyAsync(
            SupplyForgeAuthorization.CreateUserPolicy);

        Assert.NotNull(policy);
        Assert.DoesNotContain(policy!.Requirements, r => r is PermissionRequirement);

        await DisposeAsync(services);
    }

    [Fact]
    public async Task SEC_10_PermPolicy_Anonymous_Denied_EvenWhenResolverGrants()
    {
        var (services, policyProvider, auth) = Build(GrantAll());

        var policy = await policyProvider.GetPolicyAsync(
            SupplyForgeAuthorization.NotificationReadPolicy);
        Assert.NotNull(policy);

        var result = await auth.AuthorizeAsync(
            new ClaimsPrincipal(new ClaimsIdentity()), resource: null, policy!);

        Assert.False(result.Succeeded);
        await DisposeAsync(services);
    }

    [Fact]
    public async Task PLT_020_PermPolicy_WithPermissionOnExactCode_Succeeds()
    {
        // Resolver grants only the exact code — proves the provider strips the
        // perm: prefix and hands the bare catalogue code to the requirement.
        var (services, policyProvider, auth) = Build(new FakePermissionResolver(
            (_, code) => code == "platform.notification.read"));

        var policy = await policyProvider.GetPolicyAsync(
            SupplyForgeAuthorization.NotificationReadPolicy);
        Assert.NotNull(policy);

        var result = await auth.AuthorizeAsync(
            Authenticated(Guid.NewGuid()), resource: null, policy!);

        Assert.True(result.Succeeded);
        await DisposeAsync(services);
    }

    [Fact]
    public async Task PLT_020_PermPolicy_MissingPermission_Denied()
    {
        var (services, policyProvider, auth) = Build(DenyAll());

        var policy = await policyProvider.GetPolicyAsync(
            SupplyForgeAuthorization.NotificationReadPolicy);
        Assert.NotNull(policy);

        var result = await auth.AuthorizeAsync(
            Authenticated(Guid.NewGuid()), resource: null, policy!);

        Assert.False(result.Succeeded);
        await DisposeAsync(services);
    }

    [Fact]
    public async Task SEC_10_PermPolicy_AuthenticatedWithoutUserIdClaim_Denied()
    {
        // Valid authentication session but no resolvable subject → the
        // resolver is never consulted; deny (fail closed).
        var (services, policyProvider, auth) = Build(GrantAll());

        var policy = await policyProvider.GetPolicyAsync(
            SupplyForgeAuthorization.ApiKeyManagePolicy);
        Assert.NotNull(policy);

        var noSubject = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "no-subject@test.local")],
            authenticationType: "Bearer"));

        var result = await auth.AuthorizeAsync(noSubject, resource: null, policy!);

        Assert.False(result.Succeeded);
        await DisposeAsync(services);
    }

    private static FakePermissionResolver DenyAll() =>
        new((_, _) => false);

    private static FakePermissionResolver GrantAll() =>
        new((_, _) => true);

    private static (IServiceProvider Services, IAuthorizationPolicyProvider PolicyProvider, IAuthorizationService Auth) Build(
        IPermissionResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(resolver);
        // The create-user handler (A0-3 Step 4) is part of the pipeline; these
        // tests only evaluate perm:* policies, so an empty bootstrap window
        // keeps its construction available without affecting the outcomes.
        services.AddSingleton<IUserAccountState>(new FakeUserAccountState(hasAnyUser: false));
        services.AddSupplyForgeAuthorization();
        var provider = services.BuildServiceProvider();
        return (
            provider,
            provider.GetRequiredService<IAuthorizationPolicyProvider>(),
            provider.GetRequiredService<IAuthorizationService>());
    }

    private static Task DisposeAsync(IServiceProvider services) =>
        ((IAsyncDisposable)services).DisposeAsync().AsTask();

    private static ClaimsPrincipal Authenticated(Guid userId) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            authenticationType: "Bearer"));
}

/// <summary>
/// Test double for IPermissionResolver (SOLID/DI: inject abstractions).
/// Shared with FailClosedAuthorizationTests, which only needs a deny-all
/// resolver to construct the authorization pipeline.
/// </summary>
internal sealed class FakePermissionResolver : IPermissionResolver
{
    private readonly Func<Guid, string, bool> _hasPermission;

    public FakePermissionResolver(Func<Guid, string, bool> hasPermission)
    {
        _hasPermission = hasPermission;
    }

    public Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_hasPermission(userId, permissionCode));
}
