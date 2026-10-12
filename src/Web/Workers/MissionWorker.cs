using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Domain;
using Neura.Modules.ProviderIntegration.Infrastructure;

namespace Neura.Web.Workers;

/// <summary>
/// Drains the database-backed mission queue. Persisted Queued state survives
/// process restarts; relational workers claim work with a conditional UPDATE,
/// so two application instances cannot execute the same mission concurrently.
/// Active work is heartbeat-protected by MissionRecoveryService, while a
/// separate MissionRecoveryWorker closes stale runs without retrying uncertain
/// provider-side effects. The in-memory test provider uses tracked entities.
/// </summary>
public sealed class MissionWorker : BackgroundService
{
    private static readonly TimeSpan IdleDelay = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MissionWorker> _logger;
    private readonly IProviderFactory _providerFactory;
    private readonly AgentRoutingEngine _router;
    private readonly INeuralEventPublisher _events;
    private readonly MissionRecoveryService _recovery;

    public MissionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<MissionWorker> logger,
        IProviderFactory providerFactory,
        AgentRoutingEngine router,
        INeuralEventPublisher events,
        MissionRecoveryService recovery)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _providerFactory = providerFactory;
        _router = router;
        _events = events;
        _recovery = recovery;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var missionId = await FindNextQueuedMissionAsync(stoppingToken);
                if (!missionId.HasValue)
                {
                    await Task.Delay(IdleDelay, stoppingToken);
                    continue;
                }

                await ProcessMissionAsync(missionId.Value, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mission worker poll failed.");
                await Task.Delay(IdleDelay, stoppingToken);
            }
        }
    }

    private async Task<Guid?> FindNextQueuedMissionAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        return await db.Missions.AsNoTracking()
            .Where(m => m.Status == MissionStatus.Queued)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefaultAsync(ct);
    }

    private async Task ProcessMissionAsync(Guid missionId, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<OrchestrationEngine>();

        Mission? mission;
        if (db.Database.IsRelational())
        {
            // Compare-and-set claim. The UPDATE predicate is re-evaluated by
            // PostgreSQL at execution time; only one worker can change Queued
            // to Running and therefore enter the provider execution path.
            var claimed = await db.Missions
                .Where(m => m.Id == missionId && m.Status == MissionStatus.Queued)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(m => m.Status, MissionStatus.Running)
                    .SetProperty(m => m.UpdatedAtUtc, DateTime.UtcNow), ct);

            if (claimed != 1)
                return;

            mission = await db.Missions.FirstOrDefaultAsync(m => m.Id == missionId, ct);
        }
        else
        {
            mission = await db.Missions.FirstOrDefaultAsync(m => m.Id == missionId, ct);
            if (mission is null || mission.Status != MissionStatus.Queued)
                return;

            mission.SetStatus(MissionStatus.Running);
            await db.SaveChangesAsync(ct);
        }

        if (mission is null)
        {
            _logger.LogWarning("Claimed mission {MissionId} was not found after its claim.", missionId);
            return;
        }

        var task = await db.Tasks
            .Where(t => t.MissionId == missionId)
            .OrderBy(t => t.Order)
            .FirstOrDefaultAsync(ct);
        if (task is null)
        {
            await FailMissionAsync(db, mission, null, "Mission has no executable tasks.", ct);
            return;
        }

        try
        {
            Agent primaryAgent;
            IAIProvider primaryProvider;
            Agent? fallbackAgent = null;
            IAIProvider? fallbackProvider = null;

            if (mission.Mode == MissionMode.Simulation)
            {
                primaryAgent = Agent.Create("Claude Simulator", "Deterministic context-exhaustion demo agent", Guid.NewGuid(),
                    "claude-simulator", "Coding Agent", 100, new[] { AgentCapability.Coding });
                fallbackAgent = Agent.Create("ChatGPT Simulator", "Deterministic continuation demo agent", Guid.NewGuid(),
                    "chatgpt-simulator", "Reasoning Agent", 100, new[] { AgentCapability.Coding, AgentCapability.Reasoning });
                primaryProvider = new SimulationAIProvider("Claude Simulator", 100);
                fallbackProvider = new SimulationAIProvider("ChatGPT Simulator", 100, completesTask: true);
            }
            else
            {
                var selected = await SelectRealAgentsAsync(db, mission, task, ct);
                if (selected is null)
                {
                    await FailMissionAsync(db, mission, task,
                        "No enabled, connected agent matches the task capability. Connect a provider and create a matching agent before starting this Real Mode mission.", ct);
                    return;
                }

                primaryAgent = selected.Primary.Agent;
                fallbackAgent = selected.Fallback?.Agent;
                task.Assign(primaryAgent.Id);

                await _events.PublishAsync(NeuralEventTypes.RoutingDecision, new
                {
                    MissionId = mission.Id,
                    OwnerUserId = mission.OwnerUserId,
                    TaskId = task.Id,
                    SelectedAgentId = primaryAgent.Id,
                    SelectedAgentName = primaryAgent.Name,
                    SelectedProvider = selected.Primary.Provider.Kind.ToString(),
                    SelectedModel = primaryAgent.ModelId,
                    SelectedScore = selected.Primary.Score,
                    Alternatives = selected.Alternatives
                }, ct);

                primaryProvider = _providerFactory.Create(selected.Primary.Provider.Kind,
                    selected.Primary.Provider.ProtectedCredentialRef);
                if (selected.Fallback is not null)
                {
                    fallbackProvider = _providerFactory.Create(selected.Fallback.Provider.Kind,
                        selected.Fallback.Provider.ProtectedCredentialRef);
                }
            }

            var result = await _recovery.RunWithHeartbeatAsync(
                mission.Id,
                heartbeatToken => engine.RunTaskAsync(mission, task, primaryAgent, primaryProvider,
                    fallbackAgent, fallbackProvider, heartbeatToken),
                ct);

            if (result.Success)
            {
                // RunTaskAsync mutates these tracked domain objects. Keep the
                // terminal transition durable even if timeline broadcasting was
                // unavailable for a disconnected browser.
                task.Complete();
                mission.SetStatus(MissionStatus.Completed);
            }
            else
            {
                task.Fail();
                mission.SetStatus(MissionStatus.Failed);
            }

            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // A graceful shutdown is persisted as cancellation when possible.
            // If storage is unavailable, the heartbeat timeout will later mark
            // the run failed without re-executing uncertain provider-side work.
            task.Cancel();
            mission.Cancel();
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
                if (mission.Status == MissionStatus.Cancelled)
                {
                    await _events.PublishAsync(NeuralEventTypes.MissionCancelled, new
                    {
                        MissionId = mission.Id,
                        OwnerUserId = mission.OwnerUserId,
                        TaskId = task.Id,
                        Error = "Execution was cancelled because the application worker is shutting down.",
                        RecoveryAction = "NotRetried"
                    }, CancellationToken.None);
                }
            }
            catch (Exception persistenceError)
            {
                _logger.LogError(persistenceError,
                    "Could not persist graceful cancellation for mission {MissionId}; stale-run recovery will handle it after restart.",
                    mission.Id);
            }

            throw;
        }
        catch (MissionRunNoLongerActiveException ex)
        {
            // Another worker/recovery sweep changed the durable mission state.
            // Do not save this context's stale tracked copy over that decision.
            _logger.LogWarning(ex, "Mission {MissionId} lost its execution heartbeat; abandoning the local result.", mission.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mission {MissionId} failed during orchestration.", mission.Id);
            await FailMissionAsync(db, mission, task, "The orchestration run failed unexpectedly.", CancellationToken.None);
        }
    }

    private async Task<AgentSelection?> SelectRealAgentsAsync(
        NeuraDbContext db,
        Mission mission,
        AgentTask task,
        CancellationToken ct)
    {
        if (!mission.OwnerUserId.HasValue)
            return null;

        var pairs = await (
            from agent in db.Agents
            join provider in db.ProviderAccounts on agent.ProviderAccountId equals provider.Id
            where provider.UserId == mission.OwnerUserId.Value
                  && provider.State == ProviderConnectionState.Connected
                  && provider.Kind != ProviderKind.Simulation
                  && agent.Enabled
            select new AgentProvider(agent, provider))
            .ToListAsync(ct);

        var matching = pairs
            .Where(x => x.Agent.Capabilities.Any(c =>
                string.Equals(c.ToString(), task.RequiredCapability, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (matching.Count == 0)
            return null;

        var largestContext = Math.Max(1, matching.Max(x => x.Agent.ContextCapacityTokens));
        var ranked = matching
            .Select(candidate => new RankedAgent(candidate, _router.Score(
                candidate.Agent,
                task.RequiredCapability,
                Math.Clamp((double)candidate.Agent.ContextCapacityTokens / largestContext, 0d, 1d),
                isHealthy: true,
                normalizedCost: Math.Clamp((double)candidate.Agent.CostProfile, 0d, 1d),
                normalizedLatency: Math.Clamp(candidate.Agent.LatencyProfileMs / 10_000d, 0d, 1d))))
            .OrderByDescending(x => x.Score.OverallScore)
            .ThenByDescending(x => x.Candidate.Agent.Priority)
            .ThenBy(x => x.Candidate.Agent.Id)
            .ToList();

        var primary = ranked[0];
        var fallback = ranked.Skip(1)
            .OrderBy(x => x.Candidate.Provider.Id == primary.Candidate.Provider.Id ? 1 : 0)
            .ThenByDescending(x => x.Score.OverallScore)
            .FirstOrDefault();

        var alternatives = ranked.Select(x => new
        {
            AgentId = x.Candidate.Agent.Id,
            AgentName = x.Candidate.Agent.Name,
            Provider = x.Candidate.Provider.Kind.ToString(),
            Model = x.Candidate.Agent.ModelId,
            Score = x.Score.OverallScore,
            Capability = x.Score.CapabilityScore,
            Context = x.Score.ContextScore,
            Reliability = x.Score.ReliabilityScore,
            Health = x.Score.HealthScore,
            Cost = x.Score.CostScore,
            Latency = x.Score.LatencyScore
        }).ToList();

        return new AgentSelection(primary, fallback, alternatives);
    }

    private async Task FailMissionAsync(
        NeuraDbContext db,
        Mission mission,
        AgentTask? task,
        string safeReason,
        CancellationToken ct)
    {
        task?.Fail();
        mission.SetStatus(MissionStatus.Failed);
        await db.SaveChangesAsync(ct);

        await _events.PublishAsync(NeuralEventTypes.TaskFailed, new
        {
            MissionId = mission.Id,
            OwnerUserId = mission.OwnerUserId,
            TaskId = task?.Id,
            Error = safeReason
        }, ct);
    }

    private sealed record AgentProvider(Agent Agent, AIProviderAccount Provider);
    private sealed record RankedAgent(AgentProvider Candidate, Neura.Modules.AgentManagement.Domain.AgentScoreBreakdown Score);
    private sealed record AgentSelection(
        RankedAgent Primary,
        RankedAgent? Fallback,
        IReadOnlyList<object> Alternatives);
}
