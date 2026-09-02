using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Infrastructure.Security;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.ProviderIntegration.Infrastructure;
using Neura.Web.Hubs;
using Neura.Web.Middleware;
using Neura.Web.Workers;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using FluentValidation;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddScoped<OrchestrationEngine>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.ICostSink, EfCostSink>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.IContextPackageSink, EfContextPackageSink>();
builder.Services.AddScoped<Neura.Modules.Orchestration.Application.INotificationSink, EfNotificationSink>();
builder.Services.Configure<Neura.Infrastructure.Email.SmtpOptions>(builder.Configuration.GetSection("Neura:Smtp"));
builder.Services.AddSingleton<Neura.Modules.Orchestration.Application.IEmailSender, Neura.Infrastructure.Email.SmtpEmailSender>();
// Registered but never called automatically anywhere in this codebase —
// see Neura.Modules.Execution.Domain.IExecutionSandbox's own doc comment.
// Requires a Docker daemon reachable from the host running NEURA.
builder.Services.AddSingleton<Neura.Modules.Execution.Domain.IExecutionSandbox,
    Neura.Modules.Execution.Infrastructure.DockerContainerSandbox>();
builder.Services.AddSingleton<IMissionQueue, InMemoryMissionQueue>();
builder.Services.AddHostedService<MissionWorker>();
builder.Services.AddScoped<IAuditLogger, EfAuditLogger>();

// ---- Database ----
builder.Services.AddDbContext<NeuraDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// ---- Data Protection (used to encrypt provider API keys at rest) ----
builder.Services.AddDataProtection()
    .SetApplicationName("Neura")
    .PersistKeysToFileSystem(new DirectoryInfo(
        builder.Configuration["Neura:DataProtectionKeyPath"] ?? Path.Combine(AppContext.BaseDirectory, "dp-keys")));
// In a multi-instance production deployment, replace PersistKeysToFileSystem
// with a shared store (e.g. PersistKeysToDbContext, Azure Blob, or Redis)
// so every instance can decrypt credentials encrypted by any other instance.
builder.Services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();

// ---- Identity ----
builder.Services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.SignIn.RequireConfirmedAccount = false; // flip on once email sending is wired up
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<NeuraDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ManageAgents", p => p.RequireRole("Admin", "Operator"));
    options.AddPolicy("ManageProviders", p => p.RequireRole("Admin"));
    options.AddPolicy("ExecuteMission", p => p.RequireRole("Admin", "Operator", "User"));
    options.AddPolicy("ViewMemory", p => p.RequireRole("Admin", "Operator", "User", "Auditor"));
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
builder.Services.AddHttpClient("reference-fetch", client => client.Timeout = TimeSpan.FromSeconds(10));
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
var redisConnectionString = builder.Configuration["Neura:Redis:ConnectionString"];
var signalRBuilder = builder.Services.AddSignalR();
if (!string.IsNullOrEmpty(redisConnectionString))
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options => options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal("neura"));
builder.Services.AddSingleton<INeuralEventPublisher, PersistingNeuralEventPublisher>();

// ---- Session (used only for OAuth state anti-CSRF token) ----
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromMinutes(10);
});

// ---- MVC ----
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
});
builder.Services.AddValidatorsFromAssemblyContaining<Neura.Web.Validation.CreateMissionValidator>();

// ---- Rate limiting (section 42) ----
// Protects login, mission creation, and provider configuration from abuse.
// Fixed-window limiter is process-local; behind multiple instances, back
// this with a distributed store (e.g. Redis) instead.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("mission-creation", opt =>
    {
        opt.PermitLimit = 20;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("provider-config", opt =>
    {
        opt.PermitLimit = 15;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
    });

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 300,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// ---- Observability (OpenTelemetry tracing) ----
// OTLP exporter is opt-in via Neura:Observability:OtlpEndpoint — without
// it, spans are recorded in-process but never shipped anywhere.
var otlpEndpoint = builder.Configuration["Neura:Observability:OtlpEndpoint"];
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Neura.Web"))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation();
        if (!string.IsNullOrEmpty(otlpEndpoint))
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
    });

// ---- Health checks ----
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default") ?? string.Empty, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    // No hand-written EF Core migrations exist yet in this repo (they'd
    // be generated via `dotnet ef migrations add InitialCreate`, which
    // requires the .NET SDK — see docs/DATABASE.md). EnsureCreatedAsync
    // creates the schema directly from the current model instead, which
    // is what actually creates AspNetRoles/AspNetUsers/etc. before
    // RoleSeeder tries to query them. Once real migrations are added,
    // switch this to `await db.Database.MigrateAsync();` and remove
    // EnsureCreatedAsync — the two are mutually exclusive.
    var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
    await db.Database.EnsureCreatedAsync();

    await Neura.Web.Startup.RoleSeeder.SeedAsync(scope.ServiceProvider);
}

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
app.UseRateLimiter();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});
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
