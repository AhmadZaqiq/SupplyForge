using Microsoft.EntityFrameworkCore;
using SupplyForge.PlatformCore.Domain;

namespace SupplyForge.PlatformCore.Infrastructure.Data;

/// <summary>
/// Platform Core module DbContext (ADR-001, ADR-003).
/// Owns identity tables and MOD-1a shared tables that Platform Core owns:
/// AuditLog, Notification, DocumentSequence, IdempotencyKey, JobRun.
/// Other modules write those tables only via Shared interfaces.
/// </summary>
public class PlatformCoreDbContext : DbContext
{
    public const string Schema = "platform";

    public PlatformCoreDbContext(DbContextOptions<PlatformCoreDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserBranchScope> UserBranchScopes => Set<UserBranchScope>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiKeyScope> ApiKeyScopes => Set<ApiKeyScope>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("User");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.UserName).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Role");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Permission>(e =>
        {
            e.ToTable("Permission");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(100).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Module).HasMaxLength(50);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<UserRole>(e =>
        {
            e.ToTable("UserRole");
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        modelBuilder.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermission");
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(x => x.RolePermissions).HasForeignKey(x => x.RoleId);
            e.HasOne(x => x.Permission).WithMany(x => x.RolePermissions).HasForeignKey(x => x.PermissionId);
        });

        modelBuilder.Entity<UserBranchScope>(e =>
        {
            e.ToTable("UserBranchScope");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.BranchId }).IsUnique();
            e.Property(x => x.BranchId).IsRequired();
            e.HasOne(x => x.User).WithMany(x => x.BranchScopes).HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("RefreshToken");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(512).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("ApiKey");
            e.HasKey(x => x.Id);
            e.Property(x => x.PublicId).HasMaxLength(64).IsRequired();
            e.Property(x => x.SecretHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.PublicId).IsUnique();
        });

        modelBuilder.Entity<ApiKeyScope>(e =>
        {
            e.ToTable("ApiKeyScope");
            e.HasKey(x => x.Id);
            e.Property(x => x.PermissionCode).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.ApiKeyId, x.PermissionCode }).IsUnique();
            e.HasOne(x => x.ApiKey).WithMany(x => x.Scopes).HasForeignKey(x => x.ApiKeyId);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorType).HasMaxLength(20).IsRequired();
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.EntityType);
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("Notification");
            e.HasKey(x => x.Id);
            e.Property(x => x.NotificationType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.UserId, x.ReadAt });
            e.HasIndex(x => x.NotificationType);
        });

        modelBuilder.Entity<DocumentSequence>(e =>
        {
            e.ToTable("DocumentSequence");
            e.HasKey(x => x.Scope);
            e.Property(x => x.Scope).HasMaxLength(50);
        });

        modelBuilder.Entity<IdempotencyKey>(e =>
        {
            e.ToTable("IdempotencyKey");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(200);
            e.Property(x => x.PayloadHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<JobRun>(e =>
        {
            e.ToTable("JobRun");
            e.HasKey(x => x.Id);
            e.Property(x => x.JobName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.JobName);
        });
    }
}
