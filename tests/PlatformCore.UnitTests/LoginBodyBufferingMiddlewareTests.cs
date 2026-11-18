using System.Text;
using Microsoft.AspNetCore.Http;
using SupplyForge.Host.Api;
using SupplyForge.Host.RateLimiting;

namespace SupplyForge.PlatformCore.UnitTests;

/// <summary>
/// A0-2b (SEC-04): login body buffering must be hard-capped and oversized /
/// over-long user names must fall back to the per-IP bucket (no account key).
/// </summary>
public sealed class LoginBodyBufferingMiddlewareTests
{
    private static async Task<DefaultHttpContext> InvokeAsync(string body, long? contentLength)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = ApiRoutes.Auth.Login;
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = contentLength;

        var middleware = new LoginBodyBufferingMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task SEC_04_LoginBody_WithinCap_ExtractsLowercasedUserName()
    {
        var context = await InvokeAsync(
            """{"userName":"Alice","password":"secret"}""",
            contentLength: null);

        Assert.True(context.Items.TryGetValue(AuthRateLimiterPolicy.LoginUserNameItemKey, out var value));
        Assert.Equal("alice", value);
    }

    [Fact]
    public async Task SEC_04_LoginBody_ContentLengthOverCap_SkipsBufferingEntirely()
    {
        var smallBody = """{"userName":"alice","password":"x"}""";
        var context = await InvokeAsync(
            smallBody,
            contentLength: LoginBodyBufferingMiddleware.MaxBufferedBodyBytes + 1);

        Assert.False(context.Items.ContainsKey(AuthRateLimiterPolicy.LoginUserNameItemKey));

        // Never read: the raw body stream is still at position 0.
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task SEC_04_LoginBody_ActualBodyOverCap_WithoutContentLength_NoUserNameKey()
    {
        // Chunked-style: ContentLength unknown, body larger than the cap.
        var oversized = "{\"userName\":\"" + new string('x', LoginBodyBufferingMiddleware.MaxBufferedBodyBytes + 64)
            + "\",\"password\":\"p\"}";
        Assert.True(Encoding.UTF8.GetByteCount(oversized) > LoginBodyBufferingMiddleware.MaxBufferedBodyBytes);

        var context = await InvokeAsync(oversized, contentLength: null);

        Assert.False(context.Items.ContainsKey(AuthRateLimiterPolicy.LoginUserNameItemKey));

        // Rewound for downstream model binding.
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task SEC_04_LoginBody_UserNameLongerThanColumnCap_NoAccountKey()
    {
        var longName = new string('u', LoginBodyBufferingMiddleware.MaxBufferedUserNameLength + 1);
        var body = $"{{\"userName\":\"{longName}\",\"password\":\"x\"}}";
        Assert.True(Encoding.UTF8.GetByteCount(body) <= LoginBodyBufferingMiddleware.MaxBufferedBodyBytes);

        var context = await InvokeAsync(body, contentLength: null);

        Assert.False(context.Items.ContainsKey(AuthRateLimiterPolicy.LoginUserNameItemKey));
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public async Task SEC_04_LoginBody_MalformedJson_NoAccountKey_Rewound()
    {
        var context = await InvokeAsync("{not-json", contentLength: null);

        Assert.False(context.Items.ContainsKey(AuthRateLimiterPolicy.LoginUserNameItemKey));
        Assert.Equal(0, context.Request.Body.Position);
    }

    [Fact]
    public void SEC_04_BodyCap_IsSmallEnoughForLoginJson()
    {
        // Guard against accidentally raising the cap to "unbounded in practice".
        Assert.True(LoginBodyBufferingMiddleware.MaxBufferedBodyBytes <= 32 * 1024);
        Assert.Equal(100, LoginBodyBufferingMiddleware.MaxBufferedUserNameLength);
    }
}
