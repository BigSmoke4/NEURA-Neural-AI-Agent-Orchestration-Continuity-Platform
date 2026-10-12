using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Web.Workers;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class MissionRecoveryTests
{
    [Fact]
    public async Task StaleRunningMission_IsFailedWithoutRetry_AndClosesActiveTaskAndHandoff()
    {
        var events = new RecordingEventPublisher();
        var options = new MissionRecoveryOptions
        {
            HeartbeatIntervalSeconds = 1,
            StaleRunningTimeoutSeconds = 3,
            RecoverySweepIntervalSeconds = 1
        };
        using var services = CreateServices(events, options);
        var recovery = CreateRecovery(services, events, options);

        Guid missionId;
        Guid taskId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            await db.Database.EnsureCreatedAsync();
            var mission = Mission.Create(Guid.NewGuid(), "Recover safely", "Do not replay uncertain work", MissionMode.Real, Guid.NewGuid());
            mission.SetStatus(MissionStatus.Running);
            var task = AgentTask.Create(mission.Id, "Provider task", "Coding", 1);
            task.Start();
            var handoff = HandoffRecord.Create(mission.Id, task.Id, Guid.NewGuid(), HandoffReason.ProviderFailure, Guid.NewGuid());
            handoff.BeginTransfer();
            db.Missions.Add(mission);
            db.Tasks.Add(task);
            db.Handoffs.Add(handoff);
            await db.SaveChangesAsync();
            missionId = mission.Id;
            taskId = task.Id;
        }

        // The service enforces a minimum stale timeout of three heartbeat
        // intervals, so let this synthetic worker exceed that window.
        await Task.Delay(TimeSpan.FromSeconds(3.2));

        Assert.Equal(1, await recovery.RecoverStaleRunningMissionsAsync(CancellationToken.None));
        Assert.Equal(0, await recovery.RecoverStaleRunningMissionsAsync(CancellationToken.None));

        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            var mission = await db.Missions.AsNoTracking().SingleAsync(m => m.Id == missionId);
            var task = await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            var handoff = await db.Handoffs.AsNoTracking().SingleAsync(h => h.MissionId == missionId);

            Assert.Equal(MissionStatus.Failed, mission.Status);
            Assert.Equal(TaskStatus2.Failed, task.Status);
            Assert.Equal(HandoffState.Failed, handoff.State);
            Assert.Contains("not automatically retried", handoff.ValidationNotes ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Equal(new[] { NeuralEventTypes.MissionFailed }, events.EventTypes);
    }

    [Fact]
    public async Task ActiveMissionHeartbeat_RefreshesLivenessDuringProviderWork()
    {
        var events = new RecordingEventPublisher();
        var options = new MissionRecoveryOptions
        {
            HeartbeatIntervalSeconds = 1,
            StaleRunningTimeoutSeconds = 10,
            RecoverySweepIntervalSeconds = 1
        };
        using var services = CreateServices(events, options);
        var recovery = CreateRecovery(services, events, options);

        Guid missionId;
        DateTime initialHeartbeat;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            await db.Database.EnsureCreatedAsync();
            var mission = Mission.Create(Guid.NewGuid(), "Keep alive", "Heartbeat while work is in flight", MissionMode.Real);
            mission.SetStatus(MissionStatus.Running);
            db.Missions.Add(mission);
            await db.SaveChangesAsync();
            missionId = mission.Id;
            initialHeartbeat = mission.UpdatedAtUtc;
        }

        var execution = recovery.RunWithHeartbeatAsync(
            missionId,
            async cancellationToken =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2.2), cancellationToken);
                return true;
            },
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromSeconds(1.4));
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            var mission = await db.Missions.AsNoTracking().SingleAsync(m => m.Id == missionId);
            Assert.Equal(MissionStatus.Running, mission.Status);
            Assert.True(mission.UpdatedAtUtc > initialHeartbeat, "Heartbeat did not refresh UpdatedAtUtc during execution.");
        }

        Assert.True(await execution);
    }

    [Fact]
    public async Task MissionBecomingTerminal_CancelsInFlightProviderOperation()
    {
        var events = new RecordingEventPublisher();
        var options = new MissionRecoveryOptions
        {
            HeartbeatIntervalSeconds = 1,
            StaleRunningTimeoutSeconds = 10,
            RecoverySweepIntervalSeconds = 1
        };
        using var services = CreateServices(events, options);
        var recovery = CreateRecovery(services, events, options);

        Guid missionId;
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            await db.Database.EnsureCreatedAsync();
            var mission = Mission.Create(Guid.NewGuid(), "Stop safely", "Stop if recovery wins", MissionMode.Real);
            mission.SetStatus(MissionStatus.Running);
            db.Missions.Add(mission);
            await db.SaveChangesAsync();
            missionId = mission.Id;
        }

        var providerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var execution = recovery.RunWithHeartbeatAsync(
            missionId,
            async cancellationToken =>
            {
                providerStarted.TrySetResult(true);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    providerCancelled.TrySetResult(true);
                    throw;
                }
            },
            CancellationToken.None);

        await providerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
            var mission = await db.Missions.SingleAsync(m => m.Id == missionId);
            mission.SetStatus(MissionStatus.Failed);
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<MissionRunNoLongerActiveException>(() => execution);
        Assert.True(providerCancelled.Task.IsCompleted, "Provider work was not cancelled after the mission left Running.");
    }

    private static ServiceProvider CreateServices(RecordingEventPublisher events, MissionRecoveryOptions options)
    {
        var services = new ServiceCollection();
        var databaseName = $"mission-recovery-{Guid.NewGuid():N}";
        services.AddLogging();
        services.AddDbContext<NeuraDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        services.AddSingleton(options);
        services.AddSingleton<INeuralEventPublisher>(events);
        return services.BuildServiceProvider();
    }

    private static MissionRecoveryService CreateRecovery(
        IServiceProvider services,
        RecordingEventPublisher events,
        MissionRecoveryOptions options)
        => new(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<MissionRecoveryService>.Instance,
            events,
            options);

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
