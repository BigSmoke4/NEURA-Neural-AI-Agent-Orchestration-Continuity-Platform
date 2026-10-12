namespace Neura.Web.Middleware;

/// <summary>
/// Baseline browser security policy. Scripts are self-hosted and same-origin;
/// inline JavaScript and event-handler attributes are blocked. Inline styles
/// remain enabled temporarily because existing Razor views and Cytoscape use
/// style attributes; migrate those to stylesheets before removing that grant.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'self'; " +
            "object-src 'none'; " +
            "frame-ancestors 'none'; " +
            "form-action 'self'; " +
            "script-src 'self'; " +
            "script-src-attr 'none'; " +
            "style-src 'self'; " +
            "style-src-attr 'unsafe-inline'; " +
            "img-src 'self' data:; " +
            "font-src 'self' data:; " +
            $"connect-src {BuildConnectSources(context.Request)}; " +
            "manifest-src 'self'; " +
            "worker-src 'self' blob:";

        await _next(context);
    }

    private static string BuildConnectSources(HttpRequest request)
    {
        const string selfSource = "'self'";
        if (!request.Host.HasValue)
            return selfSource;

        var host = request.Host.Host.Trim('[', ']');
        if (Uri.CheckHostName(host) == UriHostNameType.Unknown)
            return selfSource;

        var authority = host.Contains(':')
            ? $"[{host}]"
            : host;
        if (request.Host.Port is int port && port >= 0)
            authority += $":{port}";

        var websocketScheme = request.IsHttps ? "wss" : "ws";
        return $"{selfSource} {websocketScheme}://{authority}";
    }
}
