using System.Globalization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace Neura.Web.RateLimiting;

/// <summary>
/// Applies the same global and named windows as the local limiter, but uses
/// Redis-backed atomic counters when a shared Redis endpoint is configured.
/// Storage errors fail closed with 503 rather than bypassing abuse controls.
/// </summary>
public sealed class DistributedRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DistributedRateLimitMiddleware> _logger;

    public DistributedRateLimitMiddleware(
        RequestDelegate next,
        ILogger<DistributedRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Keep process liveness independent of Redis availability. All other
        // endpoints, including readiness, require the shared quota store.
        if (string.Equals(context.Request.Path.Value, "/health/live", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<DisableRateLimitingAttribute>() is not null)
        {
            await _next(context);
            return;
        }

        var policyName = endpoint?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
        RateLimitQuota? namedQuota = null;
        if (!string.IsNullOrWhiteSpace(policyName))
        {
            if (!NeuraRateLimitPolicies.TryGetNamed(policyName, out var configuredQuota))
                throw new InvalidOperationException("An endpoint refers to an unknown named rate-limit policy.");
            namedQuota = configuredQuota;
        }
        else
        {
            policyName = null;
        }

        RateLimitDecision decision;
        try
        {
            var counterStore = context.RequestServices.GetRequiredService<IRedisRateLimitCounterStore>();
            decision = await counterStore.TryAcquireAsync(
                NeuraRateLimitPolicies.GetClientPartition(context.Connection.RemoteIpAddress),
                policyName,
                NeuraRateLimitPolicies.Global,
                namedQuota,
                context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Do not include connection strings, exception messages, or client
            // addresses in this log record. The endpoint is denied, not allowed
            // through on an unavailable shared quota store.
            _logger.LogError(
                "Shared rate-limit storage failed with {FailureType}; rejecting request.",
                exception.GetType().Name);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(
                "Request rate limiting is temporarily unavailable.",
                context.RequestAborted);
            return;
        }

        if (!decision.IsAllowed)
        {
            var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(decision.RetryAfter.TotalSeconds));
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Retry-After"] = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            await context.Response.WriteAsync("Rate limit exceeded.", context.RequestAborted);
            return;
        }

        await _next(context);
    }

}
