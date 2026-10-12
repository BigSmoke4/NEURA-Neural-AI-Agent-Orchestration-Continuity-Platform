using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Infrastructure.Security;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Memory.Application;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.ProviderIntegration.Infrastructure;
using Neura.Web.Hubs;
using Neura.Web.Middleware;
using Neura.Web.Networking;
using Neura.Web.Observability;
using Neura.Web.RateLimiting;
using Neura.Web.Workers;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using FluentValidation;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Fail closed on deployment defaults that weaken host filtering or make
// encrypted provider credentials dependent on an ephemeral key ring.
Neura.Web.Startup.ProductionConfigurationValidator.Validate(builder.Configuration, builder.Environment);

// Trust forwarded scheme/client-IP headers only from explicitly configured
// reverse proxies. ASP.NET Core's loopback defaults remain trusted; do not
// clear the allow-list or accept forwarded headers from arbitrary clients.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    var trustedProxyAddresses = builder.Configuration
        .GetSection("Neura:ForwardedHeaders:KnownProxies")
        .Get<string[]>() ?? Array.Empty<string>();

    foreach (var addressText in trustedProxyAddresses)
    {
        if (!IPAddress.TryParse(addressText, out var address))
            throw new InvalidOperationException($"Invalid trusted proxy IP address in Neura:ForwardedHeaders:KnownProxies: '{addressText}'.");
        options.KnownProxies.Add(address);
    }
});

// ---- Structured logging (Serilog) ----
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// ---- Options ----
builder.Services.Configure<ContextThresholdOptions>(builder.Configuration.GetSection("Neura:ContextThresholds"));
builder.Services.AddSingleton(sp => builder.Configuration.GetSection("Neura:ContextThresholds").Get<ContextThresholdOptions>() ?? new ContextThresholdOptions());
builder.Services.AddSingleton(sp => builder.Configuration.GetSection("Neura:ModelPricing").Get<Neura.Modules.ProviderIntegration.Domain.ModelPricingOptions>() ?? new Neura.Modules.ProviderIntegration.Domain.ModelPricingOptions());
builder.Services.AddSingleton(new AgentRoutingWeights());
builder.Services.AddSingleton<AgentRoutingEngine>();
builder.Services.AddSingleton<ContextContinuityEngine>();
builder.Services.AddScoped<IMemoryService, EfMemoryService>();
builder.Services.AddHostedService<MemoryExpirationWorker>();
builder.Services.AddScoped<OrchestrationEngine>();
builder.Services.AddSingleton(sp => builder.Configuration.GetSection("Neura:MissionRecovery").Get<MissionRecoveryOptions>() ?? new MissionRecoveryOptions());
builder.Services.AddSingleton<MissionRecoveryService>();
builder.Services.AddHostedService<MissionRecoveryWorker>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.ICostSink, EfCostSink>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.IContextPackageSink, EfContextPackageSink>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.IHandoffSink, EfHandoffSink>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.INotificationSink, EfNotificationSink>();
builder.Services.Configure<Neura.Infrastructure.Email.SmtpOptions>(builder.Configuration.GetSection("Neura:Smtp"));
builder.Services.AddSingleton<Neura.Modules.Orchestration.Application.IEmailSender, Neura.Infrastructure.Email.SmtpEmailSender>();
// Registered but never called automatically anywhere in this codebase —
// see Neura.Modules.Execution.Domain.IExecutionSandbox's own doc comment.
// Requires a Docker daemon reachable from the host running NEURA.
builder.Services.AddSingleton<Neura.Modules.Execution.Domain.IExecutionSandbox,
    Neura.Modules.Execution.Infrastructure.DockerContainerSandbox>();
builder.Services.AddHostedService<MissionWorker>();
builder.Services.AddScoped<IAuditLogger, EfAuditLogger>();

// ---- Database ----
// Prefer a mounted secret file for the database password when configured;
// the resolved connection string is held only in process memory and is never
// written to logs or rendered in the UI.
var databaseConnectionString = Neura.Web.Startup.DatabaseConnectionStringResolver.Resolve(
    builder.Configuration,
    requirePassword: !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"));
builder.Services.AddDbContext<NeuraDbContext>(options =>
{
    if (builder.Environment.IsEnvironment("Testing"))
    {
        options.UseInMemoryDatabase("neura-browser-e2e");
    }
    else
    {
        options.UseNpgsql(databaseConnectionString);
    }
});

// ---- Data Protection (used to encrypt provider API keys at rest) ----
var dataProtectionBuilder = builder.Services.AddDataProtection()
    .SetApplicationName("Neura");
if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
{
    var certificatePath = builder.Configuration["Neura:DataProtection:CertificatePath"]!;
    var certificatePassword = Neura.Web.Startup.SecretValueProvider.Read(
        builder.Configuration,
        "Neura:DataProtection:CertificatePassword",
        "Neura:DataProtection:CertificatePasswordFile");
    var certificate = new System.Security.Cryptography.X509Certificates.X509Certificate2(
        certificatePath,
        certificatePassword,
        System.Security.Cryptography.X509Certificates.X509KeyStorageFlags.EphemeralKeySet);
    if (!certificate.HasPrivateKey)
        throw new InvalidOperationException("The Data Protection certificate must include its private key.");
    using (var publicKey = certificate.GetRSAPublicKey())
    {
        if (publicKey is null || publicKey.KeySize < 2048)
            throw new InvalidOperationException("The Data Protection certificate must use RSA with at least a 2048-bit key.");
    }
    if (DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
        throw new InvalidOperationException("The Data Protection certificate is not currently valid.");

    dataProtectionBuilder.ProtectKeysWithCertificate(certificate);
}

dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(
    builder.Configuration["Neura:DataProtectionKeyPath"] ?? Path.Combine(AppContext.BaseDirectory, "dp-keys")));
// Multi-instance production deployments must mount this path from a shared
// persistent store and provision the same private certificate on every node.
builder.Services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();

// ---- Identity ----
var configuredCookieSecurePolicy = builder.Configuration["Neura:Security:CookieSecurePolicy"];
CookieSecurePolicy cookieSecurePolicy;
if (string.IsNullOrWhiteSpace(configuredCookieSecurePolicy))
{
    cookieSecurePolicy = builder.Environment.IsProduction()
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
}
else if (!Enum.TryParse(configuredCookieSecurePolicy, ignoreCase: true, out cookieSecurePolicy) ||
         !Enum.IsDefined(typeof(CookieSecurePolicy), cookieSecurePolicy))
{
    throw new InvalidOperationException("Neura:Security:CookieSecurePolicy must be Always, SameAsRequest, or None.");
}

var requireConfirmedEmail = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
builder.Services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(3));
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.SignIn.RequireConfirmedAccount = requireConfirmedEmail;
        options.SignIn.RequireConfirmedEmail = requireConfirmedEmail;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<NeuraDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManageAgents", p => p.RequireRole("Admin", "Operator", "User"));
    // Provider credentials and agents are self-service, but controllers scope
    // every record to the authenticated owner; Admin remains able to operate
    // the system without the client supplying a user id.
    options.AddPolicy("ManageProviders", p => p.RequireRole("Admin", "Operator", "User"));
    options.AddPolicy("ExecuteMission", p => p.RequireRole("Admin", "Operator", "User"));
    options.AddPolicy("ViewMemory", p => p.RequireRole("Admin", "Operator", "User", "Auditor"));
    options.AddPolicy("ManageMemory", p => p.RequireRole("Admin", "Operator", "User"));
    options.AddPolicy("ManageNotifications", p => p.RequireRole("Admin", "Operator", "User"));
    options.AddPolicy("ViewCredentials", p => p.RequireRole("Admin"));
    options.AddPolicy("ViewAuditLogs", p => p.RequireRole("Admin", "Auditor"));
    options.AddPolicy("ManageSystem", p => p.RequireRole("Admin"));
});

// ---- Provider adapters ----
// AnthropicAIProvider/OpenAIProvider/GoogleAIProvider take a (decrypted)
// API key at construction time, so they're resolved through a small
// factory rather than AddHttpClient<T> (which only supports an
// HttpClient-only ctor). The factory decrypts via ICredentialProtector
// immediately before use and the plaintext key is never persisted,
// logged, or cached beyond the single request that needed it.
builder.Services.AddHttpClient("anthropic");
builder.Services.AddHttpClient("openai");
builder.Services.AddHttpClient("google");
builder.Services.AddHttpClient("oauth-token-exchange");
builder.Services.AddHttpClient("reference-fetch", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.MaxResponseContentBufferSize = 20_000;
    })
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = PublicInternetSocketConnector.ConnectAsync
    });
builder.Services.AddSingleton<IProviderFactory>(sp =>
{
    var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
    var protector = sp.GetRequiredService<ICredentialProtector>();
    var pricing = sp.GetRequiredService<Neura.Modules.ProviderIntegration.Domain.ModelPricingOptions>();
    return new ProviderFactory(httpClientFactory, protector.Unprotect, pricing);
});
builder.Services.AddSingleton(sp => new SimulationAIProvider("Claude Simulator", contextWindow: 100));
builder.Services.AddSingleton(sp => new SimulationAIProvider("ChatGPT Simulator", contextWindow: 100));

// ---- Real-time ----
// Redis backplane is opt-in via Neura:Redis:ConnectionString — without
// it, SignalR events only reach clients connected to the same instance
// (correct for one instance, insufficient once you scale out).
var redisConnectionString = builder.Configuration["Neura:Redis:ConnectionString"]?.Trim();
var signalRBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options => options.Configuration.ChannelPrefix = RedisChannel.Literal("neura"));
    builder.Services.AddSingleton<IConnectionMultiplexer>(
        _ => ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<IRedisRateLimitCounterStore, RedisRateLimitCounterStore>();
}
builder.Services.AddSingleton<INeuralEventPublisher, PersistingNeuralEventPublisher>();

// ---- Session (used only for OAuth state anti-CSRF token) ----
// Use the same optional Redis endpoint for shared session state when scaled
// out; otherwise keep the convenient single-process development cache.
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "neura:session:";
    });
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

// ---- MVC ----
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddValidatorsFromAssemblyContaining<Neura.Web.Validation.CreateMissionValidator>();

// ---- Rate limiting (section 42) ----
// The named policies and global quota are shared with Redis-backed mode.
// Without Redis, ASP.NET Core enforces the same per-IP limits process-locally.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy(NeuraRateLimitPolicies.Login, context => RateLimitPartition.GetFixedWindowLimiter(
        NeuraRateLimitPolicies.GetClientPartition(context.Connection.RemoteIpAddress),
        _ => NeuraRateLimitPolicies.CreateFixedWindowOptions(
            NeuraRateLimitPolicies.GetNamed(NeuraRateLimitPolicies.Login))));

    options.AddPolicy(NeuraRateLimitPolicies.AccountRecovery, context => RateLimitPartition.GetFixedWindowLimiter(
        NeuraRateLimitPolicies.GetClientPartition(context.Connection.RemoteIpAddress),
        _ => NeuraRateLimitPolicies.CreateFixedWindowOptions(
            NeuraRateLimitPolicies.GetNamed(NeuraRateLimitPolicies.AccountRecovery))));

    options.AddPolicy(NeuraRateLimitPolicies.MissionCreation, context => RateLimitPartition.GetFixedWindowLimiter(
        NeuraRateLimitPolicies.GetClientPartition(context.Connection.RemoteIpAddress),
        _ => NeuraRateLimitPolicies.CreateFixedWindowOptions(
            NeuraRateLimitPolicies.GetNamed(NeuraRateLimitPolicies.MissionCreation))));

    options.AddPolicy(NeuraRateLimitPolicies.ProviderConfiguration, context => RateLimitPartition.GetFixedWindowLimiter(
        NeuraRateLimitPolicies.GetClientPartition(context.Connection.RemoteIpAddress),
        _ => NeuraRateLimitPolicies.CreateFixedWindowOptions(
            NeuraRateLimitPolicies.GetNamed(NeuraRateLimitPolicies.ProviderConfiguration))));

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: NeuraRateLimitPolicies.GetClientPartition(context.Connection.RemoteIpAddress),
            factory: _ => NeuraRateLimitPolicies.CreateFixedWindowOptions(NeuraRateLimitPolicies.Global)));
});

// ---- Observability (OpenTelemetry tracing and metrics) ----
// OTLP export is opt-in via Neura:Observability:OtlpEndpoint. Without an
// endpoint, instruments remain local and are not shipped to a collector.
var otlpEndpoint = builder.Configuration["Neura:Observability:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Neura.Web"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter(
            NeuraMetrics.MeterName,
            OrchestrationMetrics.MeterName,
            "Microsoft.AspNetCore.Hosting",
            "Microsoft.AspNetCore.Server.Kestrel",
            "System.Net.Http");
        if (!string.IsNullOrEmpty(otlpEndpoint))
            metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
    });

// ---- Health checks ----
builder.Services.AddHealthChecks()
    .AddNpgSql(databaseConnectionString, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

// A configured Redis endpoint is a required shared dependency for the SignalR
// backplane, session store, and cluster-wide rate limits. Resolve its
// multiplexer once at startup so outages fail fast instead of causing a new
// connection attempt per request.
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    try
    {
        _ = app.Services.GetRequiredService<IConnectionMultiplexer>();
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException(
            $"Could not connect to configured Redis dependency ({exception.GetType().Name}); startup aborted.");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
    await Neura.Web.Startup.DatabaseSchemaInitializer.InitializeAsync(db, app.Environment);

    await Neura.Web.Startup.RoleSeeder.SeedAsync(scope.ServiceProvider);
}

// Apply trusted reverse-proxy scheme/IP headers before HSTS, HTTPS
// redirection, rate limiting, and security logging.
app.UseForwardedHeaders();

// ---- Global exception handling (section 50) ----
// Friendly error page to users; full exception logged server-side only.
// Never leaks stack traces, connection strings, or provider credentials.
app.UseMiddleware<GlobalExceptionMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

// ---- Security headers (section 59) ----
app.UseMiddleware<SecurityHeadersMiddleware>();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
if (string.IsNullOrWhiteSpace(redisConnectionString))
    app.UseRateLimiter();
else
    app.UseMiddleware<DistributedRateLimitMiddleware>();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
}).WithMetadata(new DisableRateLimitingAttribute());
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Brain}/{action=Index}/{id?}");
app.MapHub<NeuralHub>("/hubs/neural");

app.Run();

public partial class Program { }
