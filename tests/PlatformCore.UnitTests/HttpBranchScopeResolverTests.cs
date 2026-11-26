using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using SupplyForge.Host.Auth;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Security;
using SupplyForge.PlatformCore.Infrastructure.Services;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A1-4 Part 2: verifies HttpBranchScopeResolver extracts the caller id from a
/// mapped JWT principal (sub → ClaimTypes.NameIdentifier per JwtBearer default)
/// and routes unauthenticated/unparseable callers to the fail-closed scope.
/// Reproduces E1: the old code read raw "sub"/"nameid" and returned
/// NoBranchScope for every authenticated real-JWT request.
/// </summary>
public sealed class HttpBranchScopeResolverTests
{
    private sealed class StubProvider(ICurrentBranchScope scope, bool invoked, Guid? invokedWith) : IBranchScopeProvider
    {
        public ICurrentBranchScope Scope { get; } = scope;
        public bool Invoked { get; } = invoked;
        public Guid? InvokedWith { get; } = invokedWith;

        public Task<ICurrentBranchScope> ResolveAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Scope);
    }

    private static ClaimsPrincipal AuthenticatedPrincipal(string claimType, string value)
    {
        var identity = new ClaimsIdentity(new[] { new Claim(claimType, value) }, "Bearer");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task E1_MappedJwtPrincipal_ExtractsNameIdentifier()
    {
        // JwtBearer maps "sub" → ClaimTypes.NameIdentifier by default; the
        // resolver must read NameIdentifier first, then raw "sub" as fallback.
        var userId = Guid.NewGuid();
        var user = AuthenticatedPrincipal(ClaimTypes.NameIdentifier, userId.ToString());
        var provider = new StubProvider(NoBranchScope.Instance, true, userId);

        var resolver = new HttpBranchScopeResolver(provider);
        var scope = await resolver.ResolveAsync(user);

        Assert.True(provider.Invoked);
        Assert.Equal(userId, provider.InvokedWith);
        Assert.Same(NoBranchScope.Instance, scope);
    }

    [Fact]
    public async Task E1_RawSubClaimFallbackWorks()
    {
        var userId = Guid.NewGuid();
        var user = AuthenticatedPrincipal("sub", userId.ToString());
        var provider = new StubProvider(NoBranchScope.Instance, true, userId);

        var resolver = new HttpBranchScopeResolver(provider);
        var scope = await resolver.ResolveAsync(user);

        Assert.True(provider.Invoked);
        Assert.Equal(userId, provider.InvokedWith);
        Assert.Same(NoBranchScope.Instance, scope);
    }

    [Fact]
    public async Task E1_NameIdentifierTakesPrecedenceOverSub()
    {
        var nameId = Guid.NewGuid();
        var sub = Guid.NewGuid();
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, nameId.ToString()),
                new Claim("sub", sub.ToString())
            },
            "Bearer");
        var user = new ClaimsPrincipal(identity);
        var provider = new StubProvider(NoBranchScope.Instance, true, nameId);

        var resolver = new HttpBranchScopeResolver(provider);
        await resolver.ResolveAsync(user);

        Assert.True(provider.Invoked);
        Assert.Equal(nameId, provider.InvokedWith);
    }

    [Fact]
    public async Task E1_UnauthenticatedPrincipal_ReturnsNoScope_DoesNotCallProvider()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity());
        var provider = new StubProvider(NoBranchScope.Instance, false, null);

        var resolver = new HttpBranchScopeResolver(provider);
        var scope = await resolver.ResolveAsync(user);

        Assert.False(provider.Invoked);
        Assert.Null(provider.InvokedWith);
        Assert.Same(NoBranchScope.Instance, scope);
        Assert.True(scope.HasNoBranchAccess);
    }

    [Fact]
    public async Task E1_UnparseableSubject_ReturnsNoScope_DoesNotCallProvider()
    {
        var user = AuthenticatedPrincipal(ClaimTypes.NameIdentifier, "not-a-guid");
        var provider = new StubProvider(NoBranchScope.Instance, false, null);

        var resolver = new HttpBranchScopeResolver(provider);
        var scope = await resolver.ResolveAsync(user);

        Assert.False(provider.Invoked);
        Assert.Null(provider.InvokedWith);
        Assert.Same(NoBranchScope.Instance, scope);
    }

    [Fact]
    public async Task E1_NullPrincipal_ReturnsNoScope_DoesNotCallProvider()
    {
        var provider = new StubProvider(NoBranchScope.Instance, false, null);
        var resolver = new HttpBranchScopeResolver(provider);

        var scope = await resolver.ResolveAsync(default!);

        Assert.False(provider.Invoked);
        Assert.Null(provider.InvokedWith);
        Assert.Same(NoBranchScope.Instance, scope);
    }
}
