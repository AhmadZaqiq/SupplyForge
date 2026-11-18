using System.Collections;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Application.Security;

namespace SupplyForge.ArchitectureTests;

/// <summary>
/// A0-3 Step 6: behavioral authorization coverage (PLT-022 deny-default,
/// SEC-10). The source scan in <see cref="EndpointAuthorizationCoverageTests"/>
/// proves each endpoint *declares* a named policy; this test proves no
/// obtainable policy actually *grants* access to both an anonymous and a
/// normal authenticated caller — an always-true named policy would silently
/// defeat the endpoint scan, the fail-closed fallback, and the permission
/// check at the same time. "Normal conditions" here: at least one user exists
/// (so the create-user bootstrap window is closed) and the caller holds no
/// permissions.
/// </summary>
public class AlwaysTruePolicyTests
{
    private static readonly string[] PermissionCodesToProbe =
    [
        PermissionCodes.PlatformNotificationRead,
        PermissionCodes.PlatformNotificationRaise,
        PermissionCodes.PlatformApiKeyManage,
        PermissionCodes.PlatformUserManage,
    ];

    [Fact]
    public async Task PLT_022_AlwaysTrueNamedPolicy_IsDetected_ByBehavioralScan()
    {
        // Regression guard for the scan itself: a policy granting anonymous
        // and authenticated callers alike (the shape of the Step-1 temporary
        // create-user policy) must be flagged, not passed over.
        await using var provider = BuildAuthorization(options =>
            options.AddPolicy("test:always-true", policy => policy.RequireAssertion(_ => true)));

        var scan = await ScanAsync(provider);

        Assert.Contains("test:always-true", scan.Violations);
    }

    [Fact]
    public async Task PLT_022_NoObtainablePolicy_GrantsBothAnonymousAndAuthenticated()
    {
        await using var provider = BuildAuthorization();

        var scan = await ScanAsync(provider);

        // Guard against a vacuous pass: every obtainable policy must have
        // been evaluated — Fallback, Default, every named policy, and the
        // provider-built perm:* policies for every catalogue code.
        foreach (var expected in ExpectedPolicyNames())
        {
            Assert.Contains(expected, scan.Evaluated);
        }

        Assert.True(
            scan.Violations.Count == 0,
            $"Policies granting anonymous and authenticated callers alike: {string.Join(", ", scan.Violations)}");
    }

    private static IEnumerable<string> ExpectedPolicyNames()
    {
        yield return "FallbackPolicy";
        yield return "DefaultPolicy";
        yield return SupplyForgeAuthorization.CreateUserPolicy;
        foreach (var code in PermissionCodesToProbe)
        {
            yield return SupplyForgeAuthorization.PermissionPolicyPrefix + code;
        }
    }

    private static ServiceProvider BuildAuthorization(Action<AuthorizationOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Normal conditions behind the handlers: the Users table is not empty
        // (bootstrap window closed) and the caller holds no permissions. The
        // stubs are registered before AddSupplyForgeAuthorization, which does
        // not register these interfaces itself (the Host's AddSupplyForge does).
        services.AddSingleton<IUserAccountState>(new StubUserAccountState(hasAnyUser: true));
        services.AddSingleton<IPermissionResolver>(new StubPermissionResolver());

        services.AddSupplyForgeAuthorization();

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Enumerates the named policies registered on <see cref="AuthorizationOptions"/>.
    /// Throws when the private registry cannot be found: an unenumerable
    /// options object would make the scan vacuous (everything passes), and a
    /// hard failure is the safe direction. The positive test registers a
    /// synthetic policy and requires its detection, so this path stays honest.
    /// (.NET 10 stores each policy as a lazily-awaited Task in a private
    /// string-keyed map.)
    /// </summary>
    private static IEnumerable<(string Name, Task<AuthorizationPolicy> Policy)> RegisteredNamedPolicies(
        AuthorizationOptions options)
    {
        var registryField = typeof(AuthorizationOptions)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f =>
                f.FieldType.IsGenericType &&
                f.FieldType.GetGenericArguments()[0] == typeof(string) &&
                f.FieldType.GetGenericArguments()[1].IsGenericType &&
                f.FieldType.GetGenericArguments()[1].GetGenericTypeDefinition() == typeof(Task<>) &&
                f.FieldType.GetGenericArguments()[1].GetGenericArguments()[0] == typeof(AuthorizationPolicy));

        if (registryField?.GetValue(options) is not IDictionary registry)
        {
            throw new InvalidOperationException(
                "Cannot enumerate AuthorizationOptions named policies (internal shape changed); " +
                "the always-true policy scan would be blind.");
        }

        foreach (DictionaryEntry entry in registry)
        {
            yield return ((string)entry.Key, (Task<AuthorizationPolicy>)entry.Value!);
        }
    }

    private sealed record ScanResult(IReadOnlyList<string> Evaluated, IReadOnlyList<string> Violations);

    private static async Task<ScanResult> ScanAsync(ServiceProvider provider)
    {
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();
        var authorization = provider.GetRequiredService<IAuthorizationService>();

        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var candidates = new Dictionary<string, AuthorizationPolicy?>(StringComparer.Ordinal)
        {
            ["FallbackPolicy"] = await policyProvider.GetFallbackPolicyAsync(),
            ["DefaultPolicy"] = await policyProvider.GetDefaultPolicyAsync(),
        };

        // Every registered named policy (create-user today, any future named
        // policy too — the endpoint source scan accepts any non-bare name, so
        // nothing else guarantees these get checked). AuthorizationOptions has
        // no public enumeration; read its private registry and fail loudly if
        // that internal shape ever changes rather than scan nothing.
        foreach (var (name, policyTask) in RegisteredNamedPolicies(options))
        {
            candidates[name] = await policyTask;
        }

        // perm:* policies are built on demand by PermissionPolicyProvider and
        // are not registered in options; ask the provider for each code.
        foreach (var code in PermissionCodesToProbe)
        {
            var name = SupplyForgeAuthorization.PermissionPolicyPrefix + code;
            candidates[name] = await policyProvider.GetPolicyAsync(name);
        }

        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var authenticated = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())],
            authenticationType: "Bearer"));

        var evaluated = new List<string>();
        var violations = new List<string>();

        foreach (var (name, policy) in candidates)
        {
            if (policy is null)
            {
                continue;
            }

            evaluated.Add(name);

            var anonymousResult = await authorization.AuthorizeAsync(anonymous, resource: null, policy);
            var authenticatedResult = await authorization.AuthorizeAsync(authenticated, resource: null, policy);

            // A policy that passes both an anonymous caller and an
            // authenticated caller with no permissions is always-true under
            // normal conditions (SEC-10 fail-closed violated).
            if (anonymousResult.Succeeded && authenticatedResult.Succeeded)
            {
                violations.Add(name);
            }
        }

        return new ScanResult(evaluated, violations);
    }

    private sealed class StubUserAccountState(bool hasAnyUser) : IUserAccountState
    {
        public Task<bool> HasAnyUserAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(hasAnyUser);
    }

    private sealed class StubPermissionResolver : IPermissionResolver
    {
        public Task<bool> HasPermissionAsync(
            Guid userId,
            string permissionCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
