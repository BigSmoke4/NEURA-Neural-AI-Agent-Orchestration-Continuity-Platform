namespace Neura.Web.Middleware;

/// <summary>
/// Section 59: baseline security headers against clickjacking, MIME
/// sniffing, and unintended cross-origin leakage. A Content-Security-Policy
/// is intentionally left commented — the Cytoscape.js and SignalR CDN
/// scripts loaded in _Layout.cshtml need their exact hosts allow-listed
/// before CSP can be turned on without breaking the neural graph.
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
        // context.Response.Headers["Content-Security-Policy"] =
        //     "default-src 'self'; script-src 'self' cdnjs.cloudflare.com; connect-src 'self' wss:;";

        await _next(context);
    }
}
