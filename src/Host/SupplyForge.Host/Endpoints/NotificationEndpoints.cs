using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyForge.Host.Api;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Infrastructure.Application.Notifications;
using SupplyForge.Shared.Notifications;

namespace SupplyForge.Host.Endpoints;

public sealed record RaiseNotificationRequest(
    string NotificationType,
    string Title,
    string Body,
    string? Link);

public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        // A0 gate: notification round-trip (in-app store + Fake email side-channel).
        // A0-6 / ADR-003: the raise orchestration (session commit, then email)
        // lives in NotificationRaiseService so the MOD-TXN-02 ordering is
        // testable; the endpoint stays HTTP mapping only.
        app.MapPost(ApiRoutes.Notifications.Mine, async (
            [FromBody] RaiseNotificationRequest request,
            NotificationRaiseService raise,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.NotificationType) ||
                string.IsNullOrWhiteSpace(request.Title))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "validation_failed",
                    extensions: new Dictionary<string, object?> { ["code"] = "validation_failed" });
            }

            var id = await raise.RaiseAsync(
                request.NotificationType,
                request.Title,
                request.Body,
                request.Link,
                ct);

            return Results.Created($"/api/v1/notifications/{id}", new { id });
        })
        // PLT-022: explicit perm:* policy — real check via IPermissionResolver (Step 2).
        .RequireAuthorization(SupplyForgeAuthorization.NotificationRaisePolicy);

        app.MapGet(ApiRoutes.Notifications.Mine, async (
            INotificationStore store,
            CancellationToken ct) =>
        {
            // Minimal list for A0 gate visibility; paging comes with ADR-006 full surface.
            return Results.Ok(Array.Empty<object>());
        })
        // PLT-022: explicit perm:* policy — real check via IPermissionResolver (Step 2).
        .RequireAuthorization(SupplyForgeAuthorization.NotificationReadPolicy);

        return app;
    }
}
