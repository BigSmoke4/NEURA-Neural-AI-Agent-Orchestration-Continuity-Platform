using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Neura.Web.RateLimiting;
using Xunit;

namespace Neura.Tests.Security;

public sealed class DistributedRateLimitMiddlewareTests
{
    [Fact]
    public void Redis_key_prefix_uses_isolated_safe_namespaces()
    {
        Assert.Equal("neura:rate-limit:v1", RedisRateLimitCounterStore.NormalizeKeyPrefix(null));
        Assert.Equal("tenant.prod:rate-limit", RedisRateLimitCounterStore.NormalizeKeyPrefix("tenant.prod:rate-limit:"));
        Assert.Throws<InvalidOperationException>(() => RedisRateLimitCounterStore.NormalizeKeyPrefix("tenant:{unsafe}"));
    }

    [Fact]
    public async Task Named_policy_uses_normalized_client_ip_and_global_and_named_quotas()
    {
        var store = new FakeRateLimitStore(new RateLimitDecision(true, TimeSpan.Zero));
        using var services = ServicesFor(store);
        var context = CreateContext(services);
        context.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.0.2.45");
        context.SetEndpoint(EndpointWith(new EnableRateLimitingAttribute(NeuraRateLimitPolicies.Login)));
        var nextCalled = false;
        var middleware = Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal("192.0.2.45", store.ClientPartition);
        Assert.Equal(NeuraRateLimitPolicies.Login, store.PolicyName);
        Assert.Equal(300, store.GlobalQuota?.PermitLimit ?? 0);
        Assert.Equal(10, store.NamedQuota?.PermitLimit ?? 0);
    }

    [Fact]
    public async Task Rejected_request_returns_retry_after_without_invoking_endpoint()
    {
        var store = new FakeRateLimitStore(new RateLimitDecision(false, TimeSpan.FromMilliseconds(5_300)));
        using var services = ServicesFor(store);
        var context = CreateContext(services);
        var nextCalled = false;
        var middleware = Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.Equal("6", context.Response.Headers["Retry-After"].ToString());
        Assert.Equal("no-store", context.Response.Headers["Cache-Control"].ToString());
    }

    [Fact]
    public async Task Store_failure_fails_closed_without_returning_exception_details()
    {
        var store = new FakeRateLimitStore(new RateLimitDecision(true, TimeSpan.Zero))
        {
            Failure = new InvalidOperationException("redis-password-must-not-leak")
        };
        using var services = ServicesFor(store);
        var context = CreateContext(services);
        var nextCalled = false;
        var middleware = Middleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body, Encoding.UTF8).ReadToEndAsync();
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.DoesNotContain("redis-password-must-not-leak", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Liveness_and_explicitly_disabled_endpoints_do_not_require_redis()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var middleware = Middleware(_ => Task.CompletedTask);

        var liveness = CreateContext(services);
        liveness.Request.Path = "/health/live";
        await middleware.InvokeAsync(liveness);

        var disabled = CreateContext(services);
        disabled.SetEndpoint(EndpointWith(new DisableRateLimitingAttribute()));
        await middleware.InvokeAsync(disabled);

        Assert.Equal(StatusCodes.Status200OK, liveness.Response.StatusCode);
        Assert.Equal(StatusCodes.Status200OK, disabled.Response.StatusCode);
    }

    private static DistributedRateLimitMiddleware Middleware(RequestDelegate next)
        => new(next, NullLogger<DistributedRateLimitMiddleware>.Instance);

    private static ServiceProvider ServicesFor(FakeRateLimitStore store)
        => new ServiceCollection()
            .AddSingleton<IRedisRateLimitCounterStore>(store)
            .BuildServiceProvider();

    private static DefaultHttpContext CreateContext(IServiceProvider services)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");
        return context;
    }

    private static Endpoint EndpointWith(object metadata)
        => new(
            requestDelegate: null,
            metadata: new EndpointMetadataCollection(new[] { metadata }),
            displayName: "rate-limit-test");

    private sealed class FakeRateLimitStore : IRedisRateLimitCounterStore
    {
        private readonly RateLimitDecision _decision;

        public FakeRateLimitStore(RateLimitDecision decision) => _decision = decision;

        public string? ClientPartition { get; private set; }
        public string? PolicyName { get; private set; }
        public RateLimitQuota? GlobalQuota { get; private set; }
        public RateLimitQuota? NamedQuota { get; private set; }
        public Exception? Failure { get; set; }

        public Task<RateLimitDecision> TryAcquireAsync(
            string clientPartition,
            string? policyName,
            RateLimitQuota globalQuota,
            RateLimitQuota? namedQuota,
            CancellationToken cancellationToken)
        {
            ClientPartition = clientPartition;
            PolicyName = policyName;
            GlobalQuota = globalQuota;
            NamedQuota = namedQuota;
            if (Failure is not null)
                throw Failure;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_decision);
        }
    }
}
