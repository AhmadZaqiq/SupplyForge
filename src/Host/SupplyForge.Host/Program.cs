using Microsoft.AspNetCore.Authorization;
using SupplyForge.Host;
using SupplyForge.Host.Api;
using SupplyForge.Host.Authorization;
using SupplyForge.Host.Endpoints;
using SupplyForge.Host.Middleware;
using SupplyForge.Host.OpenApi;
using SupplyForge.Host.RateLimiting;
using SupplyForge.PlatformCore.Infrastructure.Data;

var builder = WebApplication.CreateBuilder(args);

// INT-052: OpenAPI from code; ADR-006 §8–9 — both auth schemes documented,
// per-endpoint security from endpoint metadata (SupplyForgeOpenApi).
builder.Services.AddOpenApi(options => options.UseSupplyForgeConventions());
builder.Services.AddSupplyForge(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseMiddleware<CorrelationAndProblemDetailsMiddleware>();

// SEC-04 (A0-2): forwarded client IPs are opt-in and proxy-gated; off in the
// A0 topology, where the socket peer is the real client.
var rateLimitOptions = AuthRateLimitOptions.Bind(builder.Configuration);
if (rateLimitOptions.EnableForwardedHeaders)
{
    app.UseForwardedHeaders();
}

// The rate-limit policy needs the login user name from the body first.
app.UseMiddleware<LoginBodyBufferingMiddleware>();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // INT-052: the generated document must be reachable in dev. Explicit
    // AllowAnonymous — the fail-closed FallbackPolicy (PLT-022) otherwise
    // challenges the unauthenticated GET (401) like any protected endpoint.
    app.MapOpenApi().AllowAnonymous();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet(ApiRoutes.Health.Liveness, () => Results.Ok(new { status = "healthy" }))
    // Intentionally public liveness probe (PLT-022 coverage: explicit AllowAnonymous).
    .AllowAnonymous();
app.MapAuthEndpoints();
app.MapNotificationEndpoints();
app.MapApiKeyEndpoints();

// PLT-020 / PLT-ROLE-1 (A0-3 Step 3): seed the permission catalogue and the
// system role bundles before serving traffic. Idempotent get-or-insert, so a
// restart or a parallel Host instance is a no-op (no migration involved).
using (var seedScope = app.Services.CreateScope())
{
    var catalogueSeed = seedScope.ServiceProvider.GetRequiredService<PlatformCatalogueSeed>();
    await catalogueSeed.EnsureSeededAsync(app.Lifetime.ApplicationStopping);
}

app.Run();

public partial class Program;
