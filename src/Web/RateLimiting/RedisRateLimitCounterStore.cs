using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Neura.Web.RateLimiting;

/// <summary>
/// Acquires global and endpoint-specific fixed-window permits in one Redis
/// Lua operation. Both keys share a Redis Cluster hash slot and contain only
/// a digest of the client address. Redis failures are propagated so the HTTP
/// middleware can fail closed instead of silently reverting to local quotas.
/// </summary>
public sealed class RedisRateLimitCounterStore : IRedisRateLimitCounterStore
{
    private const string DefaultKeyPrefix = "neura:rate-limit:v1";

    private const string AcquireScript = @"
local global_limit = tonumber(ARGV[1])
local global_window_ms = tonumber(ARGV[2])
local named_limit = tonumber(ARGV[3])
local named_window_ms = tonumber(ARGV[4])

local global_count = tonumber(redis.call('GET', KEYS[1]) or '0')
local global_ttl = redis.call('PTTL', KEYS[1])
if global_ttl == -1 then
    redis.call('PEXPIRE', KEYS[1], global_window_ms)
    global_ttl = global_window_ms
end

local global_retry_ms = 0
if global_count >= global_limit then
    global_retry_ms = global_ttl
    if global_retry_ms < 1 then global_retry_ms = global_window_ms end
end

local named_count = 0
local named_ttl = -2
local named_retry_ms = 0
if named_limit > 0 then
    named_count = tonumber(redis.call('GET', KEYS[2]) or '0')
    named_ttl = redis.call('PTTL', KEYS[2])
    if named_ttl == -1 then
        redis.call('PEXPIRE', KEYS[2], named_window_ms)
        named_ttl = named_window_ms
    end
    if named_count >= named_limit then
        named_retry_ms = named_ttl
        if named_retry_ms < 1 then named_retry_ms = named_window_ms end
    end
end

if global_retry_ms > 0 or named_retry_ms > 0 then
    return {0, math.max(global_retry_ms, named_retry_ms)}
end

-- Set expirations before increments so a partial script error cannot leave
-- an immortal counter. NX preserves the fixed window's original expiry.
redis.call('SET', KEYS[1], '0', 'PX', global_window_ms, 'NX')
if named_limit > 0 then
    redis.call('SET', KEYS[2], '0', 'PX', named_window_ms, 'NX')
end
redis.call('INCR', KEYS[1])
if named_limit > 0 then
    redis.call('INCR', KEYS[2])
end

return {1, 0}
";

    private readonly IDatabase _database;
    private readonly string _keyPrefix;

    public RedisRateLimitCounterStore(IConnectionMultiplexer connection, IConfiguration configuration)
    {
        _database = connection.GetDatabase();
        _keyPrefix = NormalizeKeyPrefix(configuration["Neura:RateLimiting:RedisKeyPrefix"]);
    }

    public async Task<RateLimitDecision> TryAcquireAsync(
        string clientPartition,
        string? policyName,
        RateLimitQuota globalQuota,
        RateLimitQuota? namedQuota,
        CancellationToken cancellationToken)
    {
        ValidateQuota(globalQuota, nameof(globalQuota));
        if (namedQuota is not null)
            ValidateQuota(namedQuota, nameof(namedQuota));
        if ((policyName is null) != (namedQuota is null))
            throw new ArgumentException("A named policy and its quota must be provided together.", nameof(policyName));

        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(clientPartition)))
            .ToLowerInvariant();
        // The shared hash tag keeps both counters in the same slot on Redis Cluster.
        var globalKey = (RedisKey)$"{_keyPrefix}:{{{digest}}}:global";
        var namedKey = namedQuota is null
            ? globalKey
            : (RedisKey)$"{_keyPrefix}:{{{digest}}}:policy:{NormalizePolicyName(policyName!)}";

        var globalWindowMilliseconds = ToMilliseconds(globalQuota.Window);
        var namedWindowMilliseconds = namedQuota is null ? 0 : ToMilliseconds(namedQuota.Window);
        var result = (RedisResult[]?)await _database.ScriptEvaluateAsync(
                AcquireScript,
                new[] { globalKey, namedKey },
                new RedisValue[]
                {
                    globalQuota.PermitLimit,
                    globalWindowMilliseconds,
                    namedQuota?.PermitLimit ?? 0,
                    namedWindowMilliseconds
                })
            .WaitAsync(cancellationToken);

        if (result is null || result.Length != 2)
            throw new InvalidOperationException("Redis returned an invalid distributed rate-limit decision.");

        var isAllowed = (long)result[0] == 1;
        if (isAllowed)
            return new RateLimitDecision(true, TimeSpan.Zero);

        var retryMilliseconds = (long)result[1];
        return new RateLimitDecision(
            false,
            TimeSpan.FromMilliseconds(Math.Max(1, retryMilliseconds)));
    }

    public static string NormalizeKeyPrefix(string? configuredPrefix)
    {
        var prefix = string.IsNullOrWhiteSpace(configuredPrefix)
            ? DefaultKeyPrefix
            : configuredPrefix.Trim().TrimEnd(':');
        if (prefix.Length is < 1 or > 128 || prefix.Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is ':' or '-' or '_' or '.')))
        {
            throw new InvalidOperationException(
                "Neura:RateLimiting:RedisKeyPrefix may contain only ASCII letters, digits, colon, dot, underscore, and hyphen.");
        }

        return prefix;
    }

    private static string NormalizePolicyName(string policyName)
        => policyName switch
        {
            NeuraRateLimitPolicies.Login => NeuraRateLimitPolicies.Login,
            NeuraRateLimitPolicies.AccountRecovery => NeuraRateLimitPolicies.AccountRecovery,
            NeuraRateLimitPolicies.MissionCreation => NeuraRateLimitPolicies.MissionCreation,
            NeuraRateLimitPolicies.ProviderConfiguration => NeuraRateLimitPolicies.ProviderConfiguration,
            _ => throw new ArgumentOutOfRangeException(nameof(policyName), "Unknown named rate-limit policy.")
        };

    private static void ValidateQuota(RateLimitQuota quota, string parameterName)
    {
        if (quota.PermitLimit < 1 || quota.Window <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(parameterName, "Rate-limit quotas must have a positive limit and window.");
    }

    private static long ToMilliseconds(TimeSpan duration)
        => Math.Max(1, checked((long)Math.Ceiling(duration.TotalMilliseconds)));
}
