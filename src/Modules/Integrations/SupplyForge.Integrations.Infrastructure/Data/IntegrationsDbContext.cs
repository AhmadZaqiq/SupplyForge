using Microsoft.EntityFrameworkCore;
using SupplyForge.Integrations.Domain;

namespace SupplyForge.Integrations.Infrastructure.Data;

/// <summary>
/// Integrations module DbContext (ADR-001, ADR-003).
/// Owns OutboxMessage, InboxMessage, IntegrationLog (MOD-1a + OUT-01).
/// Does NOT own AuditLog/Notification/Idempotency/DocumentSequence/JobRun —
/// those remain Platform Core tables written only via Shared interfaces.
/// </summary>
public class IntegrationsDbContext : DbContext
{
    public const string Schema = "integrations";

    public IntegrationsDbContext(DbContextOptions<IntegrationsDbContext> options)
        : base(options)
    {
    }

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IntegrationLog> IntegrationLogs => Set<IntegrationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessage");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40).IsRequired();
            e.HasIndex(x => new { x.Status, x.OccurredAt });
            e.HasIndex(x => x.EventType);
        });

        modelBuilder.Entity<InboxMessage>(e =>
        {
            e.ToTable("InboxMessage");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageKey).HasMaxLength(200).IsRequired();
            e.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            e.Property(x => x.PayloadJson).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.MessageKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.ReceivedAt });
        });

        modelBuilder.Entity<IntegrationLog>(e =>
        {
            e.ToTable("IntegrationLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Adapter).HasMaxLength(100).IsRequired();
            e.Property(x => x.Level).HasMaxLength(20).IsRequired();
            e.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => x.OccurredAt);
        });
    }
}
