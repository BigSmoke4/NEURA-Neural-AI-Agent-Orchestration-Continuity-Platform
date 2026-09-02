using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Infrastructure;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Web.Workers;

/// <summary>
/// Background hosted service draining the mission queue. This is what
/// lets Mission Control return immediately (HTTP 202-style UX) while
/// orchestration actually runs out-of-band, per section 43.
///
/// Loads the SAME persisted Mission/AgentTask rows that
/// MissionsController.Create wrote — not fresh in-memory copies — so
/// that anything attached to the task before it runs (e.g. reference
/// material via TaskReferenceController) is actually picked up, and so
/// status changes made during orchestration are saved back.
/// </summary>
public sealed class MissionWorker : BackgroundService
{
    private readonly IMissionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MissionWorker> _logger;
    private readonly IProviderFactory _providerFactory;

    public MissionWorker(IMissionQueue queue, IServiceScopeFactory scopeFactory, ILogger<MissionWorker> logger,
        IProviderFactory providerFactory)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _providerFactory = providerFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var queued in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
                var engine = scope.ServiceProvider.GetRequiredService<OrchestrationEngine>();

                var mission = await db.Missions.FirstOrDefaultAsync(m => m.Id == queued.MissionId, stoppingToken);
                if (mission is null)
                {
                    _logger.LogWarning("Queued mission {MissionId} was not found in the database — skipping.", queued.MissionId);
                    continue;
                }

                var task = await db.Tasks.FirstOrDefaultAsync(t => t.MissionId == queued.MissionId, stoppingToken);
                if (task is null)
                {
                    task = AgentTask.Create(mission.Id, queued.Title, nameof(AgentCapability.Coding), 1);
                    db.Tasks.Add(task);
                    await db.SaveChangesAsync(stoppingToken);
                }

                Agent primaryAgent;
                IAIProvider primaryProvider;
                Agent? fallbackAgent = null;
                IAIProvider? fallbackProvider = null;

                if (mission.Mode == MissionMode.Simulation)
                {
                    primaryAgent = Agent.Create("Claude Simulator", "Simulated coding agent", Guid.NewGuid(),
                        "claude-simulator", "Coding Agent", 100, new[] { AgentCapability.Coding });
                    fallbackAgent = Agent.Create("ChatGPT Simulator", "Simulated reasoning agent", Guid.NewGuid(),
                        "gpt-simulator", "Reasoning Agent", 100, new[] { AgentCapability.Reasoning });
                    primaryProvider = new SimulationAIProvider("Claude Simulator", 100);
                    fallbackProvider = new SimulationAIProvider("ChatGPT Simulator", 100);
                }
                else
                {
                    var accounts = await db.ProviderAccounts
                        .Where(a => a.UserId == mission.OwnerUserId &&
                                    a.State == ProviderConnectionState.Connected &&
                                    a.Kind != ProviderKind.Simulation)
                        .OrderBy(a => a.CreatedAtUtc)
                        .Take(2)
                        .ToListAsync(stoppingToken);

                    if (accounts.Count == 0)
                    {
                        task.Fail();
                        mission.SetStatus(MissionStatus.Failed);
                        _logger.LogWarning("Real-mode mission {MissionId} has no connected provider account for user {UserId}.",
                            mission.Id, mission.OwnerUserId);
                        continue;
                    }

                    primaryAgent = CreateRealAgent(accounts[0].Kind);
                    primaryProvider = _providerFactory.Create(accounts[0].Kind, accounts[0].ProtectedCredentialRef);

                    if (accounts.Count > 1)
                    {
                        fallbackAgent = CreateRealAgent(accounts[1].Kind);
                        fallbackProvider = _providerFactory.Create(accounts[1].Kind, accounts[1].ProtectedCredentialRef);
                    }
                }

                var result = await engine.RunTaskAsync(mission, task, primaryAgent, primaryProvider,
                    fallbackAgent, fallbackProvider, stoppingToken);

                // Persist the status transitions RunTaskAsync made on the
                // tracked mission/task entities. The worker also makes the
                // completion transition durable so a transport/publisher
                // failure cannot leave a successful background run looking
                // permanently in progress.
                if (result.Success)
                {
                    task.Complete();
                    mission.SetStatus(MissionStatus.Completed);

                    var completionExists = await db.ExecutionEvents.AnyAsync(
                        e => e.MissionId == mission.Id &&
                             e.TaskId == task.Id &&
                             e.EventType == NeuralEventTypes.TaskCompleted,
                        stoppingToken);

                    if (!completionExists)
                    {
                        db.ExecutionEvents.Add(Neura.Modules.Observability.Domain.ExecutionEvent.Record(
                            mission.Id, task.Id, result.CompletedByAgentId,
                            NeuralEventTypes.TaskCompleted,
                            System.Text.Json.JsonSerializer.Serialize(new
                            { MissionId = mission.Id, TaskId = task.Id,
                              RecoveredByWorker = true })));
                    }
                }
                else
                {
                    task.Fail();
                    mission.SetStatus(MissionStatus.Failed);
                }

                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host shutdown: do not turn a normal cancellation into a mission failure.
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Mission {MissionId} failed in background worker", queued.MissionId);
                try
                {
                    using var failureScope = _scopeFactory.CreateScope();
                    var failureDb = failureScope.ServiceProvider.GetRequiredService<NeuraDbContext>();
                    var failedMission = await failureDb.Missions.FirstOrDefaultAsync(m => m.Id == queued.MissionId);
                    if (failedMission is not null)
                    {
                        failedMission.SetStatus(MissionStatus.Failed);
                        var failedTask = await failureDb.Tasks.FirstOrDefaultAsync(t => t.MissionId == queued.MissionId);
                        failedTask?.Fail();
                        await failureDb.SaveChangesAsync();
                    }
                }
                catch (Exception persistEx)
                {
                    _logger.LogError(persistEx, "Could not persist failed status for mission {MissionId}", queued.MissionId);
                }
            }
        }
    }

    private static Agent CreateRealAgent(ProviderKind kind)
    {
        var (name, modelId) = kind switch
        {
            ProviderKind.Anthropic => ("Anthropic Agent", "claude-opus-5"),
            ProviderKind.OpenAI => ("OpenAI Agent", "gpt-5"),
            ProviderKind.Google => ("Google Gemini Agent", "gemini-2.5-pro"),
            _ => throw new NotSupportedException($"Provider {kind} cannot run a real mission.")
        };

        return Agent.Create(name, $"Real {kind} coding agent", Guid.NewGuid(),
            modelId, "Coding Agent", 100, new[] { AgentCapability.Coding });
    }

}
