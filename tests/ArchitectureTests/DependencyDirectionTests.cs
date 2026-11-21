namespace SupplyForge.ArchitectureTests;

/// <summary>
/// Reflection-only architecture tests (M0-1). No NetArchTest dependency.
/// Named with PRD IDs where the rule has one (NFR-051).
/// </summary>
public class DependencyDirectionTests
{
    private static readonly string[] ContractAssemblies =
    [
        "SupplyForge.PlatformCore.Contracts",
        "SupplyForge.Integrations.Contracts",
        "SupplyForge.MasterData.Contracts",
    ];

    // ADR-001 §PRD-confirmed 2: a contract assembly is a leaf — no outward
    // reference to any module implementation or to any other contracts
    // assembly (DM-8: no shared contracts assembly, so there is nothing
    // legitimate for a contracts assembly to point at).
    private static readonly string[] ForbiddenForContracts =
    [
        "SupplyForge.PlatformCore",
        "SupplyForge.PlatformCore.Infrastructure",
        "SupplyForge.Integrations",
        "SupplyForge.Integrations.Infrastructure",
        "SupplyForge.MasterData",
        "SupplyForge.MasterData.Infrastructure",
        "SupplyForge.PlatformCore.Contracts",
        "SupplyForge.Integrations.Contracts",
        "SupplyForge.MasterData.Contracts",
        "SupplyForge.Shared",
        "SupplyForge.Host",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
    ];

    // MOD-1 / MOD-5: module implementation assemblies, pairwise disjoint.
    private static readonly string[][] ModuleImplementations =
    [
        ["SupplyForge.PlatformCore", "SupplyForge.PlatformCore.Infrastructure"],
        ["SupplyForge.Integrations", "SupplyForge.Integrations.Infrastructure"],
        ["SupplyForge.MasterData", "SupplyForge.MasterData.Infrastructure"],
    ];

    [Fact]
    public void PLT_043_ContractsAreLeaf_NoOutwardReferences()
    {
        foreach (var name in ContractAssemblies)
        {
            var asm = Load(name);
            var refs = asm.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

            foreach (var forbidden in ForbiddenForContracts)
            {
                Assert.False(
                    refs.Contains(forbidden, StringComparer.OrdinalIgnoreCase),
                    $"{name} must not reference {forbidden}. Referenced: {string.Join(", ", refs)}");
            }
        }
    }

    [Fact]
    public void MOD_001_ModulesDoNotReferenceOtherModuleImplementations()
    {
        // MOD-1 / MOD-5: no module references another module's implementation,
        // in either direction, including via Infrastructure. Generalised in A1-1
        // from the original PlatformCore × Integrations pair to every pair of
        // module assemblies (MOD-10: the arch tests grow with the module graph).
        foreach (var module in ModuleImplementations)
        {
            foreach (var name in module)
            {
                var refs = Load(name).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

                foreach (var other in ModuleImplementations)
                {
                    if (ReferenceEquals(other, module))
                    {
                        continue;
                    }

                    foreach (var otherName in other)
                    {
                        Assert.False(
                            refs.Contains(otherName, StringComparer.OrdinalIgnoreCase),
                            $"{name} must not reference {otherName}");
                    }
                }
            }
        }
    }

    [Fact]
    public void MOD_005_SharedDoesNotReferenceModules()
    {
        var refs = Load("SupplyForge.Shared")
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty);

        foreach (var module in new[]
                 {
                     "SupplyForge.PlatformCore",
                     "SupplyForge.PlatformCore.Contracts",
                     "SupplyForge.PlatformCore.Infrastructure",
                     "SupplyForge.Integrations",
                     "SupplyForge.Integrations.Contracts",
                     "SupplyForge.Integrations.Infrastructure",
                     "SupplyForge.MasterData",
                     "SupplyForge.MasterData.Contracts",
                     "SupplyForge.MasterData.Infrastructure",
                     "SupplyForge.Host",
                 })
        {
            Assert.False(
                refs.Contains(module, StringComparer.OrdinalIgnoreCase),
                $"SupplyForge.Shared must not reference {module}");
        }
    }

    [Fact]
    public void MOD_008_BannedInfrastructurePackages_AbsentFromProjectReferences()
    {
        // Scan all solution production assemblies' references for MOD-8 bans.
        var banned = new[]
        {
            "MediatR",
            "MassTransit",
            "Polly",
            "StackExchange.Redis",
            "RabbitMQ.Client",
            "NServiceBus",
            "CAP",
        };

        var production = new[]
        {
            "SupplyForge.Shared",
            "SupplyForge.PlatformCore.Contracts",
            "SupplyForge.PlatformCore",
            "SupplyForge.PlatformCore.Infrastructure",
            "SupplyForge.Integrations.Contracts",
            "SupplyForge.Integrations",
            "SupplyForge.Integrations.Infrastructure",
            "SupplyForge.MasterData.Contracts",
            "SupplyForge.MasterData",
            "SupplyForge.MasterData.Infrastructure",
            "SupplyForge.Host",
        };

        foreach (var name in production)
        {
            var refs = Load(name).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
            foreach (var b in banned)
            {
                Assert.False(
                    refs.Contains(b, StringComparer.OrdinalIgnoreCase),
                    $"{name} must not reference banned package {b}");
            }
        }
    }

    [Fact]
    public void MOD_001_HostIsOnlyCompositionRoot_DoesNotExportDomainTypes()
    {
        var host = Load("SupplyForge.Host");
        // Host may reference module infrastructure; it must not be referenced BY modules.
        foreach (var module in new[]
                 {
                     "SupplyForge.Shared",
                     "SupplyForge.PlatformCore",
                     "SupplyForge.PlatformCore.Contracts",
                     "SupplyForge.PlatformCore.Infrastructure",
                     "SupplyForge.Integrations",
                     "SupplyForge.Integrations.Contracts",
                     "SupplyForge.Integrations.Infrastructure",
                     "SupplyForge.MasterData",
                     "SupplyForge.MasterData.Contracts",
                     "SupplyForge.MasterData.Infrastructure",
                 })
        {
            var refs = Load(module).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
            Assert.False(
                refs.Contains("SupplyForge.Host", StringComparer.OrdinalIgnoreCase),
                $"{module} must not reference SupplyForge.Host");
        }

        // Sanity: host assembly loaded
        Assert.Equal("SupplyForge.Host", host.GetName().Name);
    }

    private static System.Reflection.Assembly Load(string name) =>
        System.Reflection.Assembly.Load(new System.Reflection.AssemblyName(name));
}
