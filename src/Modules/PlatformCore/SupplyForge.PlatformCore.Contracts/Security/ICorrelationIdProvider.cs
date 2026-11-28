namespace SupplyForge.PlatformCore.Contracts.Security;

/// <summary>
/// Provides the current request correlation id as set by
/// <see cref="Host.Middleware.CorrelationAndProblemDetailsMiddleware"/>
/// (stored in <c>HttpContext.Items["CorrelationId"]</c>).
/// Implemented by the Host using <see cref="Microsoft.AspNetCore.Http.IHttpContextAccessor"/>.
/// Returns null when no HTTP context is available (e.g. background jobs, tests).
/// </summary>
public interface ICorrelationIdProvider
{
    /// <summary>
    /// The current correlation id, or null when no HTTP context is available.
    /// </summary>
    string? GetCorrelationId();
}
