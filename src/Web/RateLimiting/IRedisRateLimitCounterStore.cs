namespace Neura.Web.RateLimiting;

public sealed record RateLimitDecision(bool IsAllowed, TimeSpan RetryAfter);

/// <summary>Atomic, shared counter acquisition for fixed-window HTTP quotas.</summary>
public interface IRedisRateLimitCounterStore
{
    Task<RateLimitDecision> TryAcquireAsync(
        string clientPartition,
        string? policyName,
        RateLimitQuota globalQuota,
        RateLimitQuota? namedQuota,
        CancellationToken cancellationToken);
}
