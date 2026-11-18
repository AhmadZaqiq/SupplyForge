using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using SupplyForge.Host.Api;
using SupplyForge.Host.Authorization;
using SupplyForge.Host.OpenApi;
using SupplyForge.PlatformCore.Infrastructure.Application.Integrations;

namespace SupplyForge.Host.Endpoints;

public sealed record IssueApiKeyRequest(
    string Name,
    IReadOnlyList<string> PermissionCodes,
    DateTimeOffset? ExpiresAt);

public static class ApiKeyEndpoints
{
    public static IEndpointRouteBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        // Issue/revoke require interactive auth (SEC-10). X-Api-Key is for
        // Integration Client callers of business APIs, not for key management.
        app.MapPost(ApiRoutes.V1 + "/api-keys", async (
            [FromBody] IssueApiKeyRequest request,
            ApiKeyService service,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name) || request.PermissionCodes.Count == 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "validation_failed",
                    extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
            }

            var issued = await service.IssueAsync(
                request.Name,
                request.PermissionCodes,
                request.ExpiresAt,
                createdByUserId: null,
                ct);

            // Secret returned once (ADR-009); never log or store plaintext.
            return Results.Created($"/api/v1/api-keys/{issued.Id}", new
            {
                id = issued.Id,
                publicId = issued.PublicId,
                secret = issued.Secret,
            });
        })
        // PLT-022: explicit perm:* policy — real check via IPermissionResolver (Step 2).
        .RequireAuthorization(SupplyForgeAuthorization.ApiKeyManagePolicy);

        app.MapDelete(ApiRoutes.V1 + "/api-keys/{id:guid}", async (
            Guid id,
            ApiKeyService service,
            CancellationToken ct) =>
        {
            var ok = await service.RevokeAsync(id, ct);
            return ok ? Results.NoContent() : Results.NotFound();
        })
        // PLT-022: explicit perm:* policy — real check via IPermissionResolver (Step 2).
        .RequireAuthorization(SupplyForgeAuthorization.ApiKeyManagePolicy);

        // INT-050 scope check surface: authenticate via X-Api-Key header (frozen ADR-009).
        // JWT is not used here — AllowAnonymous so the fail-closed fallback does not
        // block API-key-only clients; the handler still rejects missing/invalid keys.
        app.MapGet(ApiRoutes.V1 + "/api-keys/me", async (
            ApiKeyService service,
            HttpContext http,
            CancellationToken ct) =>
        {
            if (!http.Request.Headers.TryGetValue("X-Api-Key", out var header))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "missing_api_key",
                    extensions: new Dictionary<string, object?> { ["code"] = "missing_api_key" });
            }

            var key = await service.AuthenticateAsync(header.ToString(), ct);
            if (key is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "invalid_api_key",
                    extensions: new Dictionary<string, object?> { ["code"] = "invalid_api_key" });
            }

            return Results.Ok(new
            {
                id = key.Id,
                publicId = key.PublicId,
                name = key.Name,
                scopes = key.Scopes.Select(s => s.PermissionCode).ToArray(),
            });
        })
        .AllowAnonymous()
        // INT-052: this route authenticates on the frozen X-Api-Key header in
        // its own handler (ADR-009), so the contract declares that scheme here
        // — the endpoint is AllowAnonymous only w.r.t. the fail-closed JWT policy.
        .AddOpenApiOperationTransformer((operation, context, ct) =>
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(SupplyForgeOpenApi.ApiKeySchemeId, context.Document)] = [],
            });
            return Task.CompletedTask;
        });

        return app;
    }
}
