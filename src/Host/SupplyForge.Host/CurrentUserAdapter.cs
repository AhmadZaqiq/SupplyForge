using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SupplyForge.PlatformCore.Contracts.Security;

namespace SupplyForge.Host;

/// <summary>
/// Reads the authenticated user id from the JWT "sub" claim (mapped to
/// <see cref="ClaimTypes.NameIdentifier"/> by JwtBearer). Returns null when no
/// user is authenticated (system scope, background jobs).
/// </summary>
public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null) return null;
            var claim = user.FindFirst(ClaimTypes.NameIdentifier) ?? user.FindFirst("sub");
            if (claim is null || !Guid.TryParse(claim.Value, out var id)) return null;
            return id;
        }
    }

    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}

/// <summary>
/// Reads the correlation id set by <see cref="Host.Middleware.CorrelationAndProblemDetailsMiddleware"/>
/// into <c>HttpContext.Items["CorrelationId"]</c>. Returns null when no HTTP
/// context is available (background jobs, tests).
/// </summary>
public sealed class HttpCorrelationIdProvider : ICorrelationIdProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCorrelationIdProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? GetCorrelationId() =>
        _httpContextAccessor.HttpContext?.Items["CorrelationId"] as string;
}
