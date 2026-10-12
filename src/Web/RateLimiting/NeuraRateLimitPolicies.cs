using System.Net;
using System.Threading.RateLimiting;

namespace Neura.Web.RateLimiting;

public sealed record RateLimitQuota(int PermitLimit, TimeSpan Window);

/// <summary>
/// The single source of truth for local and Redis-backed request quotas.
/// Named policies are per client IP and always combine with the global quota.
/// </summary>
public static class NeuraRateLimitPolicies
{
    public const string Login = "login";
    public const string AccountRecovery = "account-recovery";
    public const string MissionCreation = "mission-creation";
    public const string ProviderConfiguration = "provider-config";

    public static RateLimitQuota Global { get; } = new(300, TimeSpan.FromMinutes(1));

    private static readonly IReadOnlyDictionary<string, RateLimitQuota> Named =
        new Dictionary<string, RateLimitQuota>(StringComparer.Ordinal)
        {
            [Login] = new(10, TimeSpan.FromMinutes(1)),
            [AccountRecovery] = new(10, TimeSpan.FromMinutes(1)),
            [MissionCreation] = new(20, TimeSpan.FromMinutes(1)),
            [ProviderConfiguration] = new(15, TimeSpan.FromMinutes(1))
        };

    public static RateLimitQuota GetNamed(string policyName)
        => Named.TryGetValue(policyName, out var configured)
            ? configured
            : throw new ArgumentOutOfRangeException(nameof(policyName), "Unknown named rate-limit policy.");

    public static bool TryGetNamed(string policyName, out RateLimitQuota quota)
    {
        if (Named.TryGetValue(policyName, out var configured))
        {
            quota = configured;
            return true;
        }

        quota = default!;
        return false;
    }

    public static string GetClientPartition(IPAddress? remoteAddress)
    {
        if (remoteAddress is null)
            return "unknown";
        if (remoteAddress.IsIPv4MappedToIPv6)
            remoteAddress = remoteAddress.MapToIPv4();
        return remoteAddress.ToString();
    }

    public static FixedWindowRateLimiterOptions CreateFixedWindowOptions(RateLimitQuota quota)
        => new()
        {
            PermitLimit = quota.PermitLimit,
            Window = quota.Window,
            QueueLimit = 0
        };
}
