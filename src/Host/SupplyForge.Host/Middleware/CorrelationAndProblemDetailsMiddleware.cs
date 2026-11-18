using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SupplyForge.Host.Middleware;

/// <summary>
/// ADR-006: ProblemDetails for errors + X-Correlation-ID accept/generate/echo.
/// </summary>
public sealed class CorrelationAndProblemDetailsMiddleware
{
    public const string CorrelationHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationAndProblemDetailsMiddleware> _logger;

    public CorrelationAndProblemDetailsMiddleware(
        RequestDelegate next,
        ILogger<CorrelationAndProblemDetailsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Items["CorrelationId"] = correlationId;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception correlation={CorrelationId}", correlationId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://httpstatuses.com/500",
                Extensions = { ["correlationId"] = correlationId },
            };

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}
