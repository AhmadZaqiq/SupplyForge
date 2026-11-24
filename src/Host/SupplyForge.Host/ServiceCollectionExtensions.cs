using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using SupplyForge.Host.Auth;
using SupplyForge.Host.Authorization;
using SupplyForge.Host.Notifications;
using SupplyForge.Host.RateLimiting;
using SupplyForge.PlatformCore.Application.Email;
using SupplyForge.PlatformCore.Application.Security;
using SupplyForge.PlatformCore.Infrastructure.Application.Auth;
using SupplyForge.PlatformCore.Infrastructure.Application.Integrations;
using SupplyForge.PlatformCore.Infrastructure.Application.Notifications;
using SupplyForge.PlatformCore.Infrastructure.Data;
using SupplyForge.PlatformCore.Infrastructure.Services;
using SupplyForge.Shared.Auditing;
using SupplyForge.Shared.Notifications;

namespace SupplyForge.Host;

public static class ServiceCollectionExtensions
{
    // A0-8 / SEC-20: the development placeholder shipped in appsettings.json.
    // The guard in AddSupplyForge fails startup outside Development while this
    // value is still configured; ArchitectureTests runs that guard against the
    // real appsettings.json, so the constant and the file cannot drift apart.
    private const string DevelopmentPlaceholderSecret =
        "SupplyForge_Dev_AccessTokenSecret_ChangeMe_0123456789";

    public static IServiceCollection AddSupplyForge(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("SupplyForge")
            ?? throw new InvalidOperationException("Connection string 'SupplyForge' is missing.");

        services.AddDbContext<PlatformCoreDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", PlatformCoreDbContext.Schema)));

        services.AddDbContext<SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext.Schema)));

        // A1-1 (MD-001/MD-002): Master Data module, same single connection and
        // per-module schema history table as the other modules (ADR-003).
        services.AddDbContext<SupplyForge.MasterData.Infrastructure.Data.MasterDataDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    SupplyForge.MasterData.Infrastructure.Data.MasterDataDbContext.Schema)));

        services.AddScoped<PlatformSharedServices>();
        services.AddScoped<IAuditWriter>(sp => sp.GetRequiredService<PlatformSharedServices>());
        services.AddScoped<SupplyForge.Shared.Idempotency.IIdempotencyStore>(sp =>
            sp.GetRequiredService<PlatformSharedServices>());
        services.AddScoped<SupplyForge.Shared.Sequencing.IDocumentSequenceService>(sp =>
            sp.GetRequiredService<PlatformSharedServices>());
        services.AddScoped<INotificationStore>(sp => sp.GetRequiredService<PlatformSharedServices>());
        services.AddScoped<SupplyForge.Shared.Jobs.IJobRunStore>(sp =>
            sp.GetRequiredService<PlatformSharedServices>());

        services.AddScoped<SupplyForge.Integrations.Infrastructure.Outbox.Outbox>();
        services.AddScoped<SupplyForge.Shared.Outbox.IOutbox>(sp =>
            sp.GetRequiredService<SupplyForge.Integrations.Infrastructure.Outbox.Outbox>());
        services.AddScoped<SupplyForge.Shared.Outbox.IInbox>(sp =>
            sp.GetRequiredService<SupplyForge.Integrations.Infrastructure.Outbox.Outbox>());

        services.AddSingleton(TimeProvider.System);

        // ADR-008 / PLT-051 / PLT-052: email side-channel configuration,
        // bound and validated at startup like AuthRateLimitOptions so an
        // unsupported Email:Mode (e.g. Smtp before the adapter exists) fails
        // closed before any traffic is served. Defaults preserve A0 behavior:
        // Fake sender, no injected failures, three attempts with backoff.
        var emailOptions = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>()
            ?? new EmailOptions();
        emailOptions.Validate();
        services.AddSingleton(emailOptions);
        services.AddSingleton<IEmailSender, FakeEmailSender>();

        var jwtIssuer = configuration["Auth:Issuer"]
            ?? throw new InvalidOperationException("Auth:Issuer is missing.");
        var jwtAudience = configuration["Auth:Audience"]
            ?? throw new InvalidOperationException("Auth:Audience is missing.");
        var jwtSecret = configuration["Auth:AccessTokenSecret"]
            ?? throw new InvalidOperationException("Auth:AccessTokenSecret is missing.");

        // Signing-key sanity at startup (ADR-004: HS256 needs >= 32 bytes).
        if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
        {
            throw new InvalidOperationException(
                "Auth:AccessTokenSecret must be at least 32 bytes (set via environment variable Auth__AccessTokenSecret).");
        }

        // A0-8 / SEC-20: outside Development the repository placeholder must
        // never sign tokens — a Production/Docker start with the checked-in
        // dev secret fails fast instead of serving real sessions. The message
        // names the setting only, never the value (SEC-23).
        if (!environment.IsDevelopment() && jwtSecret == DevelopmentPlaceholderSecret)
        {
            throw new InvalidOperationException(
                "Auth:AccessTokenSecret is still the repository development placeholder. " +
                "Set it via the environment (Auth__AccessTokenSecret) or a secret store before running outside Development.");
        }

        services.AddSingleton<IAccessTokenIssuer>(sp =>
            new JwtAccessTokenIssuer(
                jwtIssuer,
                jwtAudience,
                jwtSecret,
                sp.GetRequiredService<TimeProvider>()));

        services.AddScoped<AuthService>(sp => new AuthService(
            sp.GetRequiredService<PlatformCoreDbContext>(),
            sp.GetRequiredService<IAuditWriter>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IAccessTokenIssuer>(),
            sp.GetRequiredService<IPermissionResolver>(),
            sp.GetRequiredService<SupplyForge.Shared.Data.IDbSessionFactory>()));

        // PLT-022 / A0-3 Step 4: bootstrap-window state behind the create-user
        // policy (Application interface, Infrastructure implementation).
        services.AddScoped<IUserAccountState, UserAccountState>();

        services.AddScoped<ApiKeyService>();

        // A0-6 / ADR-003 / MOD-TXN-02: raise = session commit then email;
        // extracted from the endpoint so the ordering is testable.
        services.AddScoped<NotificationRaiseService>();

        // ADR-008 / PLT-051 / PLT-052: delivery state machine shared by the
        // inline raise attempt, the TD-2 mark-sent endpoint, and the retry
        // worker (adapter call always outside any session, MOD-TXN-02).
        services.AddScoped<NotificationDeliveryService>();

        // PLT-052 / INT-003: async retry with exponential backoff + jitter;
        // max attempts leaves the row Failed — dead-letter, visible in the
        // read API. Registered unconditionally; Email:Mode decides behavior.
        services.AddHostedService<NotificationRetryWorker>();

        // PLT-020 / SEC-10 (A0-3 Step 2): perm:* policy evaluation resolves
        // current permissions server-side per request (ADR-004).
        services.AddScoped<IPermissionResolver, PermissionResolver>();

        // PLT-020 / PLT-ROLE-1 (A0-3 Step 3): permission catalogue + system
        // role-bundle seed, invoked once at Host startup (Program.cs).
        services.AddScoped<PlatformCatalogueSeed>();

        // A1-1: company singleton + three-branch first-run seed (MD-001,
        // MD-002), next to the platform catalogue seed in Program.cs.
        services.AddScoped<SupplyForge.MasterData.Infrastructure.Data.MasterDataSeed>();

        // A1-4: location deactivation guard port (MD-042).
        // Placeholder implementation; Inventory provides real guard in A1-5.
        services.AddScoped<SupplyForge.MasterData.Contracts.Location.ILocationDeactivationGuard,
            SupplyForge.MasterData.Infrastructure.Location.DefaultLocationDeactivationGuard>();

        // ADR-003 (A0-6, extended by A1-1): the ambient session joins every
        // module DbContext on one shared connection/transaction. Host owns the
        // implementation and the enlistment list because it is the only
        // project referencing the module DbContexts (MOD-1 acyclic graph).
        services.AddScoped<SupplyForge.Shared.Data.IDbSessionFactory>(sp => new Data.AmbientDbSessionFactory(
            sp.GetRequiredService<PlatformCoreDbContext>(),
            sp.GetRequiredService<SupplyForge.Integrations.Infrastructure.Data.IntegrationsDbContext>(),
            sp.GetRequiredService<SupplyForge.MasterData.Infrastructure.Data.MasterDataDbContext>()));

        // SEC-10 / ADR-004 / ADR-006: standard JWT Bearer handler, HS256 only,
        // explicit 2-minute clock skew.
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = JwtAuthDefaults.CreateValidationParameters(
                    jwtSecret, jwtIssuer, jwtAudience);
            });

        // PLT-022 / SEC-10: fail-closed FallbackPolicy (no metadata) and
        // DefaultPolicy (bare RequireAuthorization); named perm:* / create-user
        // policies; explicit AllowAnonymous only on intentionally public endpoints.
        services.AddSupplyForgeAuthorization();

        // SEC-04 / PLT-012 (A0-2): built-in RateLimiting middleware guarding
        // login/refresh with two windows (IP + account). In-memory per
        // instance (MOD-8: no Redis). RejectionStatusCode defaults to 503 in
        // the framework, so 429 is set explicitly here.
        var rateLimitOptions = AuthRateLimitOptions.Bind(configuration);
        services.AddSingleton(_ => new AuthRateLimiterPolicy(rateLimitOptions));
        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>()
            .Configure<AuthRateLimiterPolicy>((options, policy) =>
            {
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.AddPolicy(AuthRateLimiterPolicy.PolicyName, policy);
            });

        if (rateLimitOptions.EnableForwardedHeaders)
        {
            services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.ForwardLimit = rateLimitOptions.ForwardLimit;
                options.KnownProxies.Clear();
                options.KnownIPNetworks.Clear();
                foreach (var proxy in rateLimitOptions.TrustedProxies)
                {
                    var address = IPAddress.Parse(proxy); // validated by AuthRateLimitOptions.Validate
                    options.KnownProxies.Add(address);
                    // Kestrel reports IPv4 peers on dual-stack sockets as
                    // ::ffff:x.y.z.w; IPAddress.Equals does not equate the two
                    // forms, so trust both representations.
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        options.KnownProxies.Add(address.MapToIPv6());
                    }
                    else if (address.IsIPv4MappedToIPv6)
                    {
                        options.KnownProxies.Add(address.MapToIPv4());
                    }
                }
            });
        }

        return services;
    }
}
