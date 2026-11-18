using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// SEC-10 / PLT-022: fail-closed FallbackPolicy and DefaultPolicy.
/// Both must deny anonymous AND authenticated callers when the requirement runs.
/// </summary>
public sealed class FailClosedAuthorizationTests
{
    private static (IAuthorizationService Auth, AuthorizationOptions Options) Build()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        // The pipeline resolves every IAuthorizationHandler; the Step-2
        // permission handler needs a resolver and the Step-4 create-user
        // handler needs user-account state. These tests never evaluate a
        // perm:* policy (that lives in PermissionAuthorizationTests), so a
        // deny-all resolver plus an empty bootstrap window keep construction
        // fail-closed while letting the create-user policy be exercised.
        services.AddSingleton<IPermissionResolver>(new FakePermissionResolver((_, _) => false));
        services.AddSingleton<IUserAccountState>(new FakeUserAccountState(hasAnyUser: false));
        services.AddSupplyForgeAuthorization();
        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value);
    }

    private static ClaimsPrincipal Anonymous() =>
        new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated() =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            authenticationType: "Bearer"));

    [Fact]
    public async Task SEC_10_Fallback_MetadataLess_Anonymous_Denied()
    {
        var (auth, options) = Build();
        var policy = Assert.IsType<AuthorizationPolicy>(options.FallbackPolicy);
        var result = await auth.AuthorizeAsync(Anonymous(), resource: null, policy);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SEC_10_Fallback_MetadataLess_Authenticated_Denied()
    {
        var (auth, options) = Build();
        var policy = Assert.IsType<AuthorizationPolicy>(options.FallbackPolicy);
        var result = await auth.AuthorizeAsync(Authenticated(), resource: null, policy);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SEC_10_BareRequireAuthorization_Authenticated_Denied()
    {
        // Bare RequireAuthorization() selects DefaultPolicy — must not mean
        // "any authenticated user".
        var (auth, options) = Build();
        var policy = Assert.IsType<AuthorizationPolicy>(options.DefaultPolicy);
        var result = await auth.AuthorizeAsync(Authenticated(), resource: null, policy);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SEC_10_DefaultPolicy_Anonymous_Denied()
    {
        var (auth, options) = Build();
        var policy = Assert.IsType<AuthorizationPolicy>(options.DefaultPolicy);
        var result = await auth.AuthorizeAsync(Anonymous(), resource: null, policy);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SEC_10_Fallback_DoesNotOverride_CreateUserCustomPolicy()
    {
        // create-user is a named policy — FallbackPolicy must not replace it.
        // With the Step-4 fake holding an empty Users table (bootstrap window
        // open) the named policy's own handler lets the principal through; a
        // FallbackPolicy override would deny anonymous here.
        var (auth, options) = Build();
        Assert.NotNull(options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy));

        var anonymous = await auth.AuthorizeAsync(
            Anonymous(), resource: null, options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy)!);
        Assert.True(anonymous.Succeeded);

        var authenticated = await auth.AuthorizeAsync(
            Authenticated(), resource: null, options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy)!);
        Assert.True(authenticated.Succeeded);
    }

    // Step-1 test "SEC_10_PermPolicy_RequiresAuthentication_NotFailClosed" was
    // superseded by A0-3 Step 2: perm:* no longer means "any authenticated
    // user" — real checks live in PermissionAuthorizationTests.

    [Fact]
    public void SEC_10_FallbackPolicy_IsConfiguredFailClosed()
    {
        var (_, options) = Build();
        Assert.NotNull(options.FallbackPolicy);
        Assert.Contains(
            options.FallbackPolicy.Requirements,
            r => r is FailClosedAuthorizationRequirement);
    }

    [Fact]
    public void SEC_10_DefaultPolicy_IsConfiguredFailClosed()
    {
        var (_, options) = Build();
        Assert.NotNull(options.DefaultPolicy);
        Assert.Contains(
            options.DefaultPolicy.Requirements,
            r => r is FailClosedAuthorizationRequirement);
    }
}
