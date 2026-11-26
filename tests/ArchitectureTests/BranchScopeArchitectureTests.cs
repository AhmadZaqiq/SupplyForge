using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SupplyForge.MasterData.Domain;
using SupplyForge.MasterData.Infrastructure.Data;
using SupplyForge.PlatformCore.Contracts.Security;
using SupplyForge.PlatformCore.Infrastructure.Security;
using SupplyForge.PlatformCore.Infrastructure.Services;
using SupplyForge.Host.Auth;

namespace SupplyForge.ArchitectureTests;

/// <summary>
/// A1-4 Part 2 architecture rules (BRN-010, Q-E option a):
/// <list type="bullet">
/// <item>every branch-owned entity in <c>MasterDataDbContext</c> is covered by
/// an EF global query filter (Warehouse directly, StorageLocation via its
/// warehouse);</item>
/// <item>the scope is injected through the <c>MasterDataDbContext</c>
/// constructor (not a settable property);</item>
/// <item>no src class exposes a static settable <see cref="ICurrentBranchScope"/>,
/// and the request-side resolvers never return the system scope.</item>
/// </list>
/// </summary>
public class BranchScopeArchitectureTests
{
    private static readonly string[] SrcAssemblies =
    [
        "SupplyForge.Shared",
        "SupplyForge.PlatformCore.Contracts",
        "SupplyForge.PlatformCore",
        "SupplyForge.PlatformCore.Application",
        "SupplyForge.PlatformCore.Infrastructure",
        "SupplyForge.Integrations.Contracts",
        "SupplyForge.Integrations",
        "SupplyForge.Integrations.Infrastructure",
        "SupplyForge.MasterData.Contracts",
        "SupplyForge.MasterData",
        "SupplyForge.MasterData.Infrastructure",
        "SupplyForge.Host",
    ];

    [Fact]
    public void BRN_010_EveryBranchOwnedEntity_IsCoveredByGlobalQueryFilter()
    {
        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer("Server=localhost;Database=Unused;User Id=sa;Password=x;TrustServerCertificate=True")
            .Options;
        var instance = new MasterDataDbContext(options, BranchScopeDefaults.NoScope);
        var model = instance.Model;

        var branchOwned = model.GetEntityTypes()
            .Where(t => t.FindProperty("BranchId") is not null)
            .Select(t => t.Name)
            .ToList();

        // Today the only branch-owned entity is Warehouse (StorageLocation is
        // covered via its Warehouse navigation). The set grows with A1-5.
        Assert.Contains("SupplyForge.MasterData.Domain.Warehouse", branchOwned);

        foreach (var entityName in branchOwned)
        {
            var entityType = model.FindEntityType(entityName)!;
            Assert.NotEmpty(entityType.GetDeclaredQueryFilters());
        }

        // The location filter exists (branch-scoped via Warehouse).
        var locationType = model.FindEntityType("SupplyForge.MasterData.Domain.StorageLocation");
        Assert.NotNull(locationType);
        Assert.NotEmpty(locationType!.GetDeclaredQueryFilters());
    }

    [Fact]
    public void BRN_010_QueryFilter_ReadsScopeFromConstructor()
    {
        // D6: the scope is injected through the constructor, not a settable
        // property on the context.
        var ctor = typeof(MasterDataDbContext).GetConstructors()
            .FirstOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(ICurrentBranchScope)));
        Assert.NotNull(ctor);
        Assert.Contains(ctor.GetParameters(), p => p.ParameterType == typeof(DbContextOptions<MasterDataDbContext>));

        var options = new DbContextOptionsBuilder<MasterDataDbContext>()
            .UseSqlServer("Server=localhost;Database=Unused;User Id=sa;Password=x;TrustServerCertificate=True")
            .Options;
        var instance = new MasterDataDbContext(options, BranchScopeDefaults.NoScope);
        var model = instance.Model;

        // Both branch-owned entities keep their declared query filters.
        foreach (var entityName in new[] { "SupplyForge.MasterData.Domain.Warehouse", "SupplyForge.MasterData.Domain.StorageLocation" })
        {
            var entityType = model.FindEntityType(entityName);
            Assert.NotNull(entityType);
            Assert.NotEmpty(entityType!.GetDeclaredQueryFilters());
        }
    }

    [Fact]
    public void Q_E_NoStaticSettableBranchScope_InSrc()
    {
        // No src class may expose a static settable ICurrentBranchScope (that
        // was the cross-branch leak). The fail-closed NoBranchScope.Instance is
        // get-only and is intentionally not flagged.
        foreach (var name in SrcAssemblies)
        {
            var asm = Assembly.Load(new AssemblyName(name));
            foreach (var type in asm.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
            {
                foreach (var prop in type.GetProperties(
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (prop.PropertyType == typeof(ICurrentBranchScope) && prop.CanWrite)
                    {
                        Assert.Fail(
                            $"Static settable ICurrentBranchScope on {type.FullName}.{prop.Name} (Q-E).");
                    }
                }

                foreach (var field in type.GetFields(
                         BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.FieldType == typeof(ICurrentBranchScope) && !field.IsInitOnly)
                    {
                        Assert.Fail(
                            $"Static settable ICurrentBranchScope field on {type.FullName}.{field.Name} (Q-E).");
                    }
                }
            }
        }

        // Request-side resolvers never return the system scope.
        // ResolveAsync returns Task<ICurrentBranchScope>; a future change that
        // types a resolver to return Task<SystemBranchScope> must fail here.
        foreach (var type in new[] { typeof(BranchScopeProvider), typeof(HttpBranchScopeResolver) })
        {
            var returnsSystem = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.ReturnType != typeof(void))
                .Select(m =>
                {
                    var returnType = m.ReturnType;
                    if (returnType.IsGenericType &&
                        returnType.GetGenericTypeDefinition() == typeof(Task<>))
                    {
                        return returnType.GetGenericArguments()[0];
                    }
                    return returnType;
                })
                .Any(t => t == typeof(SystemBranchScope));
            Assert.False(returnsSystem, $"{type.Name} must not return the system scope (Q-E).");
        }
    }
}
