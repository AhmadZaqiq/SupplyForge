using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyForge.Host.Api;
using SupplyForge.Host.Authorization;
using SupplyForge.PlatformCore.Domain;
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
            HttpContext http,
            INotificationStore store,
            CancellationToken ct) =>
        {
            // ADR-008 read API (A0): the signed-in user reads broadcast plus
            // own notifications; bounded list, paging with the ADR-006 full
            // surface later.
            if (!PrincipalSubject.TryGetUserId(http.User, out var subjectId))
            {
                // Unreachable through the perm: policy (it denies an
                // unresolvable subject before the endpoint runs) — fail
                // closed anyway rather than expose another user's rows.
                return Results.Forbid();
            }

            var items = await store.ListVisibleToUserAsync(subjectId, limit: 50, ct);
            return Results.Ok(items);
        })
        // PLT-022: explicit perm:* policy — real check via IPermissionResolver (Step 2).
        .RequireAuthorization(SupplyForgeAuthorization.NotificationReadPolicy);

        // TD-2 / ADR-008: manual mark-sent is an explicit workflow (kept
        // until Q-5 decides email in P1, never tech debt to delete): the
        // operator records delivery that happened outside the adapter —
        // a pending row when email is off, or recovery of a Failed
        // dead-letter row. Same perm:* policy as raise: one operator
        // capability, so the seeded PLT-020 catalogue stays as-is.
        app.MapPost(ApiRoutes.Notifications.MarkSent, async (
            [FromRoute] Guid id,
            HttpContext http,
            NotificationDeliveryService delivery,
            CancellationToken ct) =>
        {
            if (!PrincipalSubject.TryGetUserId(http.User, out var actorId))
            {
                // Unreachable through the perm: policy (same fail-closed
                // guard as GET above) — never act without a subject.
                return Results.Forbid();
            }

            var result = await delivery.MarkSentAsync(id, actorId.ToString(), ct);
            return result switch
            {
                NotificationMarkSentResult.NotFound => Results.NotFound(),
                _ => Results.Ok(new { id, deliveryStatus = NotificationDeliveryStatus.Sent }),
            };
        })
        // PLT-022: explicit perm:* policy — real check via IPermissionResolver (Step 2).
        .RequireAuthorization(SupplyForgeAuthorization.NotificationRaisePolicy);

        return app;
    }
}
