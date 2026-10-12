using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Web.Workers;
using Testcontainers.PostgreSql;
using Xunit;

namespace Neura.Tests.Integration;

/// <summary>
/// Relational smoke test against an actual PostgreSQL instance started
/// on demand through Testcontainers, not EF Core's InMemory provider.
/// Creates the current schema with EnsureCreated, round-trips an Agent, and
/// exercises stale-mission recovery's relational compare-and-set, transaction,
/// task, and handoff updates. It does not exercise EF migrations (none are
/// checked in yet).
///
/// Requires a Docker daemon reachable from `dotnet test` (including CI).
/// Container startup failures intentionally fail the test fixture rather
/// than producing a false-green PostgreSQL test result.
/// </summary>
public class PostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("neura_test")
        .WithUsername("neura")
        .WithPassword("test_password")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    [Fact]
    public async Task CanPersistAgentAndRecoverStaleMission_AgainstRealPostgres()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var db = new NeuraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var agent = Agent.Create("Test Agent", "desc", Guid.NewGuid(), "model-x", "Coding Agent", 50_000,
            new[] { AgentCapability.Coding, AgentCapability.Testing });
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var reloaded = await db.Agents.FirstOrDefaultAsync(a => a.Id == agent.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Test Agent", reloaded!.Name);
        Assert.Contains(AgentCapability.Coding, reloaded.Capabilities);
        Assert.Contains(AgentCapability.Testing, reloaded.Capabilities);

        var mission = Mission.Create(Guid.NewGuid(), "Stale run", "Recover without replay", MissionMode.Real, Guid.NewGuid());
        mission.SetStatus(MissionStatus.Running);
        var task = AgentTask.Create(mission.Id, "Provider task", nameof(AgentCapability.Coding), 1);
        task.Start();
        var handoff = HandoffRecord.Create(mission.Id, task.Id, agent.Id, HandoffReason.ProviderFailure, Guid.NewGuid());
        handoff.BeginTransfer();
        db.Missions.Add(mission);
        db.Tasks.Add(task);
        db.Handoffs.Add(handoff);
        await db.SaveChangesAsync();

        var staleTimestamp = DateTime.UtcNow.AddHours(-1);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"Missions\" SET \"UpdatedAtUtc\" = {staleTimestamp} WHERE \"Id\" = {mission.Id}");

        var events = new RecordingEventPublisher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<NeuraDbContext>(options => options.UseNpgsql(_container.GetConnectionString()));
        using var serviceProvider = services.BuildServiceProvider();
        var recovery = new MissionRecoveryService(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MissionRecoveryService>.Instance,
            events,
            new MissionRecoveryOptions
            {
                HeartbeatIntervalSeconds = 1,
                StaleRunningTimeoutSeconds = 3,
                RecoverySweepIntervalSeconds = 1
            });

        Assert.Equal(1, await recovery.RecoverStaleRunningMissionsAsync(CancellationToken.None));
        Assert.Equal(new[] { NeuralEventTypes.MissionFailed }, events.EventTypes);

        var failedMission = await db.Missions.AsNoTracking().SingleAsync(m => m.Id == mission.Id);
        var failedTask = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        var failedHandoff = await db.Handoffs.AsNoTracking().SingleAsync(h => h.Id == handoff.Id);
        Assert.Equal(MissionStatus.Failed, failedMission.Status);
        Assert.Equal(TaskStatus2.Failed, failedTask.Status);
        Assert.Equal(HandoffState.Failed, failedHandoff.State);
    }

    private sealed class RecordingEventPublisher : INeuralEventPublisher
    {
        public List<string> EventTypes { get; } = new();

        public Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            EventTypes.Add(eventType);
            return Task.CompletedTask;
        }
    }
}
