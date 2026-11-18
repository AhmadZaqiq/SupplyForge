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
    ];

    private static readonly string[] ForbiddenForContracts =
    [
        "SupplyForge.PlatformCore",
        "SupplyForge.PlatformCore.Infrastructure",
        "SupplyForge.Integrations",
        "SupplyForge.Integrations.Infrastructure",
        "SupplyForge.Shared",
        "SupplyForge.Host",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
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
        // PlatformCore must not depend on Integrations (or vice versa), including via Infrastructure.
        var platform = new[]
        {
            "SupplyForge.PlatformCore",
            "SupplyForge.PlatformCore.Infrastructure",
        };
        var integrations = new[]
        {
            "SupplyForge.Integrations",
            "SupplyForge.Integrations.Infrastructure",
        };

        foreach (var name in platform)
        {
            var refs = Load(name).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
            foreach (var other in integrations)
            {
                Assert.False(
                    refs.Contains(other, StringComparer.OrdinalIgnoreCase),
                    $"{name} must not reference {other}");
            }
        }

        foreach (var name in integrations)
        {
            var refs = Load(name).GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);
            foreach (var other in platform)
            {
                Assert.False(
                    refs.Contains(other, StringComparer.OrdinalIgnoreCase),
                    $"{name} must not reference {other}");
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
