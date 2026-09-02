using System.Net;

namespace Neura.Web.Middleware;

/// <summary>
/// Section 50: catches unhandled exceptions anywhere in the pipeline,
/// logs full technical detail server-side only, and returns a generic
/// friendly response to the client — never a stack trace, connection
/// string, or credential value.
/// </summary>
public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid();
            _logger.LogError(ex, "Unhandled exception. CorrelationId={CorrelationId} Path={Path}", correlationId, context.Request.Path);

            if (context.Response.HasStarted)
                throw;

            context.Response.Clear();
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            if (context.Request.Path.StartsWithSegments("/api") ||
                (context.Request.Headers.Accept.ToString()?.Contains("application/json") ?? false))
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync($"{{\"error\":\"An unexpected error occurred.\",\"correlationId\":\"{correlationId}\"}}");
            }
            else
            {
                context.Response.Redirect($"/Home/Error?correlationId={correlationId}");
            }
        }
    }
}
