using Microsoft.Extensions.Configuration;
using Neura.Web.RateLimiting;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace Neura.Tests.Integration;

/// <summary>
/// Runs the actual Lua-backed quota store against Redis. Two independent
/// multiplexers model separate web instances sharing the same atomic counters.
/// Requires a Docker daemon reachable from the test process.
/// </summary>
public sealed class RedisRateLimitIntegrationTests : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7.4-alpine")
        .Build();

    public Task InitializeAsync() => _redis.StartAsync();

    public async Task DisposeAsync() => await _redis.DisposeAsync();

    [Fact]
    public async Task SeparateInstancesShareNamedAndGlobalQuotas_AndKeepClientPartitionsIsolated()
    {
        var firstConnection = await CreateConnectionAsync();
        var secondConnection = await CreateConnectionAsync();
        try
        {
            var prefix = $"test:{Guid.NewGuid():N}";
            var firstStore = CreateStore(firstConnection, prefix);
            var secondStore = CreateStore(secondConnection, prefix);
            var global = new RateLimitQuota(4, TimeSpan.FromMinutes(1));
            var login = new RateLimitQuota(2, TimeSpan.FromMinutes(1));

            var namedResults = await Task.WhenAll(Enumerable.Range(0, 24).Select(index =>
                (index % 2 == 0 ? firstStore : secondStore).TryAcquireAsync(
                    "198.51.100.20", NeuraRateLimitPolicies.Login, global, login, CancellationToken.None)));
            Assert.Equal(2, namedResults.Count(result => result.IsAllowed));

            // Named permits consume the same global window as every other request.
            var globalResults = await Task.WhenAll(Enumerable.Range(0, 24).Select(index =>
                (index % 2 == 0 ? firstStore : secondStore).TryAcquireAsync(
                    "198.51.100.20", null, global, null, CancellationToken.None)));
            Assert.Equal(2, globalResults.Count(result => result.IsAllowed));

            // A different normalized client partition has a separate quota.
            var otherClient = await secondStore.TryAcquireAsync(
                "198.51.100.21", null, global, null, CancellationToken.None);
            Assert.True(otherClient.IsAllowed);
        }
        finally
        {
            await firstConnection.CloseAsync();
            await secondConnection.CloseAsync();
        }
    }

    [Fact]
    public async Task RedisOutageFailsTheStoreClosed_AndTheMultiplexerRecoversAfterRestart()
    {
        var connection = await CreateConnectionAsync();
        try
        {
            var store = CreateStore(connection, $"test:{Guid.NewGuid():N}");
            var quota = new RateLimitQuota(20, TimeSpan.FromMinutes(1));
            Assert.True((await store.TryAcquireAsync("203.0.113.4", null, quota, null, CancellationToken.None)).IsAllowed);

            await _redis.StopAsync();
            await Assert.ThrowsAnyAsync<Exception>(() => store.TryAcquireAsync(
                "203.0.113.4", null, quota, null, CancellationToken.None));

            await _redis.StartAsync();
            var recovered = await store.TryAcquireAsync(
                "203.0.113.4", null, quota, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(20));
            Assert.True(recovered.IsAllowed);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private async Task<IConnectionMultiplexer> CreateConnectionAsync()
    {
        var configuration = ConfigurationOptions.Parse(_redis.GetConnectionString());
        configuration.AbortOnConnectFail = false;
        configuration.ConnectRetry = 1;
        configuration.ConnectTimeout = 3_000;
        configuration.SyncTimeout = 3_000;
        configuration.AsyncTimeout = 3_000;
        return await ConnectionMultiplexer.ConnectAsync(configuration);
    }

    private static RedisRateLimitCounterStore CreateStore(IConnectionMultiplexer connection, string prefix)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Neura:RateLimiting:RedisKeyPrefix"] = prefix
            })
            .Build();
        return new RedisRateLimitCounterStore(connection, configuration);
    }
}
