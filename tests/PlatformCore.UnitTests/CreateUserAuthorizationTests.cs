using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A0-3 Step 4: the create-user policy handler. Anonymous creation only while
/// no user exists (bootstrap window, PLT-010); afterwards an authenticated
/// caller with platform.user.manage (PLT-020); everything else fails closed
/// as 401/403 (SEC-13). The service-layer revalidation of the same condition
/// lives in the SQL integration tests.
/// </summary>
public sealed class CreateUserAuthorizationTests
{
    private static (IAuthorizationService Auth, AuthorizationOptions Options) Build(
        bool hasAnyUser,
        IPermissionResolver resolver)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(resolver);
        services.AddSingleton<IUserAccountState>(new FakeUserAccountState(hasAnyUser));
        services.AddSupplyForgeAuthorization();
        var provider = services.BuildServiceProvider();
        return (
            provider.GetRequiredService<IAuthorizationService>(),
            provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value);
    }

    private static ClaimsPrincipal Anonymous() =>
        new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated(Guid? userId = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.Email, "someone@test.local") };
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    private static FakePermissionResolver DenyAll() =>
        new((_, _) => false);

    [Fact]
    public async Task PLT_022_CreateUser_Anonymous_AllowedOnlyWhileNoUsersExist()
    {
        var emptyWindow = Build(hasAnyUser: false, DenyAll());
        var openPolicy = emptyWindow.Options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy);
        Assert.NotNull(openPolicy);

        var allowed = await emptyWindow.Auth.AuthorizeAsync(
            Anonymous(), resource: null, openPolicy!);
        Assert.True(allowed.Succeeded, "first-run bootstrap must allow anonymous creation");

        var closedWindow = Build(hasAnyUser: true, DenyAll());
        var closedPolicy = closedWindow.Options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy);
        Assert.NotNull(closedPolicy);

        var denied = await closedWindow.Auth.AuthorizeAsync(
            Anonymous(), resource: null, closedPolicy!);
        Assert.False(denied.Succeeded, "anonymous creation must stop once any user exists");
    }

    [Fact]
    public async Task PLT_010_CreateUser_AuthenticatedWithUserManage_AllowedWhenUsersExist()
    {
        var userId = Guid.NewGuid();
        var requested = new List<(Guid UserId, string Code)>();
        var resolver = new FakePermissionResolver((id, code) =>
        {
            requested.Add((id, code));
            return id == userId && code == PermissionCodes.PlatformUserManage;
        });

        var (auth, options) = Build(hasAnyUser: true, resolver);
        var policy = options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy);
        Assert.NotNull(policy);

        var result = await auth.AuthorizeAsync(Authenticated(userId), resource: null, policy!);

        Assert.True(result.Succeeded, "platform.user.manage must allow user creation");
        // The handler must ask for exactly the seeded manage permission.
        Assert.Contains((userId, PermissionCodes.PlatformUserManage), requested);
    }

    [Fact]
    public async Task SEC_13_CreateUser_AuthenticatedWithoutPermission_DeniedWhenUsersExist()
    {
        var (auth, options) = Build(hasAnyUser: true, DenyAll());
        var policy = options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy);
        Assert.NotNull(policy);

        var result = await auth.AuthorizeAsync(
            Authenticated(Guid.NewGuid()), resource: null, policy!);

        // Not succeeded and not anonymous → the pipeline sends 403 (SEC-13).
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task SEC_13_CreateUser_AuthenticatedWithoutSubjectClaim_DeniedWhenUsersExist()
    {
        // Grant-all resolver must not matter: an unresolvable subject fails
        // closed before the permission check (same rule as perm:* policies).
        var (auth, options) = Build(
            hasAnyUser: true, new FakePermissionResolver((_, _) => true));
        var policy = options.GetPolicy(SupplyForgeAuthorization.CreateUserPolicy);
        Assert.NotNull(policy);

        var result = await auth.AuthorizeAsync(
            Authenticated(userId: null), resource: null, policy!);

        Assert.False(result.Succeeded);
    }
}

/// <summary>
/// Test double for <see cref="IUserAccountState"/> (shared with
/// FailClosedAuthorizationTests, which needs an empty-window fake to
/// construct the authorization pipeline).
/// </summary>
internal sealed class FakeUserAccountState(bool hasAnyUser) : IUserAccountState
{
    public Task<bool> HasAnyUserAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(hasAnyUser);
}
