using System.Text;
using System.Text.Json;
using SupplyForge.Host.Api;

namespace SupplyForge.Host.RateLimiting;

/// <summary>
/// SEC-04 (A0-2): buffers the login body before the rate limiter runs so the
/// policy can key its account dimension on the user name. Reads the body
/// asynchronously, then rewinds it for model binding.
///
/// A0-2b: buffering is hard-capped at <see cref="MaxBufferedBodyBytes"/> so an
/// oversized login body cannot grow middleware memory without bound. When the
/// cap is exceeded (or Content-Length already announces it), the body is left
/// unbuffered / unparsed and the limiter falls back to the per-IP bucket.
/// This bounds only this middleware's buffering — it does not replace Kestrel's
/// request-body size limits for the rest of the pipeline.
/// </summary>
public sealed class LoginBodyBufferingMiddleware
{
    /// <summary>Max bytes this middleware will buffer for username extraction.</summary>
    public const int MaxBufferedBodyBytes = 8 * 1024;

    /// <summary>Max user-name length accepted as an account-dimension key (aligns with User.UserName column).</summary>
    public const int MaxBufferedUserNameLength = 100;

    private readonly RequestDelegate _next;

    public LoginBodyBufferingMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPost(context.Request.Method)
            && context.Request.Path.StartsWithSegments(ApiRoutes.Auth.Login))
        {
            await BufferLoginUserNameAsync(context);
        }

        await _next(context);
    }

    private static async Task BufferLoginUserNameAsync(HttpContext context)
    {
        var request = context.Request;

        // Content-Length already over the cap: do not EnableBuffering and do
        // not read — zero middleware memory for the body (IP bucket downstream).
        if (request.ContentLength is long announced && announced > MaxBufferedBodyBytes)
        {
            return;
        }

        request.EnableBuffering();

        // Read at most cap+1 bytes so the allocated buffer is a hard bound even
        // when Content-Length is absent (chunked) or understates the body.
        var buffer = new byte[MaxBufferedBodyBytes + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await request.Body.ReadAsync(
                buffer.AsMemory(total, buffer.Length - total),
                context.RequestAborted);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        // Rewind for model binding regardless of outcome.
        request.Body.Position = 0;

        // More bytes than the cap were present: do not parse a user name.
        if (total > MaxBufferedBodyBytes)
        {
            return;
        }

        var userName = ExtractUserName(Encoding.UTF8.GetString(buffer, 0, total));
        if (userName is not null)
        {
            context.Items[AuthRateLimiterPolicy.LoginUserNameItemKey] = userName;
        }
    }

    private static string? ExtractUserName(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("userName", out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                var userName = value.GetString()?.Trim();
                if (string.IsNullOrEmpty(userName)
                    || userName.Length > MaxBufferedUserNameLength)
                {
                    return null;
                }

                return userName.ToLowerInvariant();
            }
        }
        catch (JsonException)
        {
            // Malformed body: the policy falls back to its per-IP bucket.
        }

        return null;
    }
}
