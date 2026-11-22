using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SupplyForge.Host;

namespace SupplyForge.ArchitectureTests;

/// <summary>
/// A0-8 item 1 / SEC-20: outside Development the Host must refuse to start
/// while Auth:AccessTokenSecret still holds the development placeholder
/// shipped in appsettings.json, so a Production/Docker start can never sign
/// real tokens with a repository secret. The failure message names the
/// setting but never echoes the value (SEC-23). The tests run the real
/// shipped appsettings.json through AddSupplyForge, so the guard's constant
/// and the file cannot drift apart without turning one of them red.
/// </summary>
public class StartupSecretGuardTests
{
    [Fact]
    public void SEC_20_Production_With_ShippedPlaceholderSecret_FailsStartup()
    {
        var configuration = BuildShippedConfiguration();
        var shippedSecret = configuration["Auth:AccessTokenSecret"];
        Assert.False(string.IsNullOrEmpty(shippedSecret), "appsettings.json must ship an Auth:AccessTokenSecret.");

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = new ServiceCollection().AddSupplyForge(
                configuration,
                new TestEnvironment(Environments.Production));
        });

        // Names the setting and the fix, without leaking the configured value.
        Assert.Contains("Auth__AccessTokenSecret", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(shippedSecret, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SEC_20_Development_With_ShippedPlaceholderSecret_Starts()
    {
        _ = new ServiceCollection().AddSupplyForge(
            BuildShippedConfiguration(),
            new TestEnvironment(Environments.Development));
    }

    [Fact]
    public void SEC_20_Production_With_ExplicitEnvironmentSecret_Starts()
    {
        // The sanctioned shape: the placeholder replaced by a real value from
        // the environment / secret store (SEC-20) before a non-dev start.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:AccessTokenSecret"] = "Production_Secret_Set_From_Environment_0123456789",
            })
            .Build();

        _ = new ServiceCollection().AddSupplyForge(
            configuration,
            new TestEnvironment(Environments.Production));
    }

    private static IConfiguration BuildShippedConfiguration()
    {
        // Copied to the test output with the Host project reference; this is
        // the file a real non-dev start reads.
        var appsettings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        Assert.True(File.Exists(appsettings), $"appsettings.json was not copied to {AppContext.BaseDirectory}.");

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json")
            .Build();
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public TestEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; } = "SupplyForge.ArchitectureTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
