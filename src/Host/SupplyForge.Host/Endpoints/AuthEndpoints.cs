using SupplyForge.Host.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SupplyForge.Host.Authorization;
using SupplyForge.Host.RateLimiting;
using SupplyForge.PlatformCore.Infrastructure.Application.Auth;

namespace SupplyForge.Host.Endpoints;

public sealed record LoginRequest(string UserName, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record CreateUserRequest(string UserName, string Email, string Password, string DisplayName);

public sealed record AuthResponse(
    Guid UserId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // Login / refresh are public by design (obtain tokens).
        app.MapPost(ApiRoutes.Auth.Login, async (
            [FromBody] LoginRequest request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var result = await auth.LoginAsync(request.UserName, request.Password, ct);
            if (!result.Success)
            {
                var status = result.Error switch
                {
                    "locked_out" => StatusCodes.Status429TooManyRequests,
                    "invalid_credentials" or "user_inactive" => StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status400BadRequest,
                };

                // A0-9 / SEC-05: LoginResult.LockedUntil is null for every
                // failure except locked_out (which requires the correct
                // password), so this field cannot enumerate accounts.
                return Results.Problem(
                    statusCode: status,
                    title: result.Error,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = result.Error,
                        ["lockedUntil"] = result.LockedUntil,
                    });
            }

            return Results.Ok(new AuthResponse(
                result.UserId!.Value,
                result.AccessToken!,
                result.RefreshToken!,
                result.AccessTokenExpiresAt!.Value));
        })
        // SEC-04 / PLT-012 (A0-2): per-IP + per-account windows (ADR-004).
        .RequireRateLimiting(AuthRateLimiterPolicy.PolicyName)
        // Intentionally public (obtain tokens); rate-limited above.
        .AllowAnonymous();

        app.MapPost(ApiRoutes.Auth.Refresh, async (
            [FromBody] RefreshRequest request,
            AuthService auth,
            CancellationToken ct) =>
        {
            var result = await auth.RefreshAsync(request.RefreshToken, ct);
            if (!result.Success)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: result.Error,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error });
            }

            // A0-8 item 4 / PLT-011: the rotated refresh response carries the
            // real user id — Guid.Empty here broke clients that key the new
            // session by it.
            return Results.Ok(new AuthResponse(
                result.UserId!.Value,
                result.AccessToken!,
                result.RefreshToken!,
                result.AccessTokenExpiresAt!.Value));
        })
        // SEC-04 (A0-2): refresh is IP-keyed (opaque single-use tokens; see
        // AuthRateLimiterPolicy.ResolveAccountKey).
        .RequireRateLimiting(AuthRateLimiterPolicy.PolicyName)
        // Intentionally public (obtain tokens); rate-limited above.
        .AllowAnonymous();

        // PLT-022 / A0-3 Step 4: named create-user policy (not bare
        // RequireAuthorization, not AllowAnonymous). The handler allows
        // anonymous creation only while the Users table is empty (bootstrap
        // window); afterwards it needs an authenticated caller with
        // platform.user.manage. The service revalidates the same condition
        // under sp_getapplock before inserting (SEC-10).
        app.MapPost(ApiRoutes.Auth.Users, async (
            [FromBody] CreateUserRequest request,
            AuthService auth,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            // Actor for the service-layer revalidation: null for anonymous or
            // an unresolvable subject — both are bootstrap-only and fail
            // closed once any user exists.
            Guid? actingUserId = PrincipalSubject.TryGetUserId(httpContext.User, out var actorId)
                ? actorId
                : null;

            var result = await auth.CreateUserAsync(
                request.UserName, request.Email, request.Password, request.DisplayName,
                actingUserId, ct);

            if (!result.Success)
            {
                var status = result.Error switch
                {
                    "user_exists" => StatusCodes.Status409Conflict,
                    // SEC-13: creation revalidation denied after the policy
                    // allowed the request (race window) — anonymous stays
                    // 401, authenticated without permission is 403.
                    "forbidden" => httpContext.User.Identity?.IsAuthenticated == true
                        ? StatusCodes.Status403Forbidden
                        : StatusCodes.Status401Unauthorized,
                    _ => StatusCodes.Status400BadRequest,
                };

                return Results.Problem(
                    statusCode: status,
                    title: result.Error,
                    extensions: new Dictionary<string, object?> { ["code"] = result.Error });
            }

            return Results.Created($"/api/v1/users/{result.UserId}", new { id = result.UserId });
        })
        .RequireAuthorization(SupplyForgeAuthorization.CreateUserPolicy);

        return app;
    }
}
