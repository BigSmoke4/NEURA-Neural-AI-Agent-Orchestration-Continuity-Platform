using Microsoft.AspNetCore.Http;
using Neura.Web.Middleware;
using Xunit;

namespace Neura.Tests.Security;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task HttpsResponse_EmitsSameOriginScriptPolicyAndMatchingWebSocketOrigin()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("neura.example", 8443);
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var policy = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("script-src-attr 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("style-src-attr 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.Contains("wss://neura.example:8443", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("cdnjs.cloudflare.com", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("style-src 'self' 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }

    [Fact]
    public async Task InvalidHostValue_CannotInjectAdditionalCspDirectives()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("neura.example; script-src 'unsafe-inline'");
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var policy = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("connect-src 'self';", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("script-src 'unsafe-inline'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HttpResponse_AllowsOnlyTheRequestHostWebSocketScheme()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost", 5080);
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var policy = context.Response.Headers["Content-Security-Policy"].ToString();
        Assert.Contains("ws://localhost:5080", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("wss://localhost:5080", policy, StringComparison.Ordinal);
    }
}
