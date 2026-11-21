using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Idempotency;
using SupplyForge.Shared.Jobs;
using SupplyForge.Shared.Notifications;
using SupplyForge.Shared.Sequencing;
using SupplyForge.Shared.Outbox;

namespace SupplyForge.ArchitectureTests;

/// <summary>
/// MOD-1a: shared-table writers are interfaces; modules must not reach into
/// another module's DbContext. This test asserts the interface surface lives
/// in Shared and that PlatformCore.Infrastructure implements Platform-owned
/// writers while Integrations.Infrastructure owns only outbox/inbox.
/// </summary>
public class Mod1aSharedInterfaceTests
{
    [Fact]
    public void MOD_1a_SharedWriterInterfaces_ExistInShared()
    {
        var asm = typeof(IAuditWriter).Assembly;
        Assert.Equal("SupplyForge.Shared", asm.GetName().Name);

        Assert.NotNull(typeof(IOutbox));
        Assert.NotNull(typeof(IInbox));
        Assert.NotNull(typeof(IAuditWriter));
        Assert.NotNull(typeof(IIdempotencyStore));
        Assert.NotNull(typeof(IDocumentSequenceService));
        Assert.NotNull(typeof(INotificationStore));
        Assert.NotNull(typeof(IJobRunStore));
    }

    [Fact]
    public void MOD_1a_PlatformInfrastructure_ImplementsPlatformOwnedWriters()
    {
        var impl = typeof(SupplyForge.PlatformCore.Infrastructure.Services.PlatformSharedServices);

        Assert.True(typeof(IAuditWriter).IsAssignableFrom(impl));
        Assert.True(typeof(IIdempotencyStore).IsAssignableFrom(impl));
        Assert.True(typeof(IDocumentSequenceService).IsAssignableFrom(impl));
        Assert.True(typeof(INotificationStore).IsAssignableFrom(impl));
        Assert.True(typeof(IJobRunStore).IsAssignableFrom(impl));
    }

    [Fact]
    public void MOD_1a_IntegrationsInfrastructure_ImplementsOutboxOnly()
    {
        var impl = typeof(SupplyForge.Integrations.Infrastructure.Outbox.Outbox);
        Assert.True(typeof(IOutbox).IsAssignableFrom(impl));
        Assert.True(typeof(IInbox).IsAssignableFrom(impl));
        Assert.False(typeof(IAuditWriter).IsAssignableFrom(impl));
        Assert.False(typeof(IIdempotencyStore).IsAssignableFrom(impl));
    }

    [Fact]
    public void MOD_1_SharedSchemasAreSeparate_ContextsNotShared()
    {
        // Distinct DbContext types per module — ownership is not centralized.
        Assert.NotEqual(
            typeof(SupplyForge.PlatformCore.Infrastructure.Data.PlatformCoreDbContext),
            typeof(SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext));
        Assert.NotEqual(
            typeof(SupplyForge.PlatformCore.Infrastructure.Data.PlatformCoreDbContext),
            typeof(SupplyForge.MasterData.Infrastructure.Data.MasterDataDbContext));
        Assert.NotEqual(
            typeof(SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext),
            typeof(SupplyForge.MasterData.Infrastructure.Data.MasterDataDbContext));

        Assert.Equal("platform", SupplyForge.PlatformCore.Infrastructure.Data.PlatformCoreDbContext.Schema);
        Assert.Equal("integrations", SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext.Schema);
        Assert.Equal("master_data", SupplyForge.MasterData.Infrastructure.Data.MasterDataDbContext.Schema);
    }
}
