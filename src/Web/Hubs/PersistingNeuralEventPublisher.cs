using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;

namespace Neura.Web.Hubs;

/// <summary>
/// Broadcasts live events over SignalR AND persists them as
/// ExecutionEvent rows so completed missions can be replayed later
/// (section 32/78) instead of only being visible while connected.
/// </summary>
public sealed class PersistingNeuralEventPublisher : INeuralEventPublisher
{
    private readonly IHubContext<NeuralHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersistingNeuralEventPublisher> _logger;

    public PersistingNeuralEventPublisher(
        IHubContext<NeuralHub> hub,
        IServiceScopeFactory scopeFactory,
        ILogger<PersistingNeuralEventPublisher> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
    {
        // Persistence is the durable source of truth for the replay timeline.
        // A SignalR transport failure must never prevent an orchestration event
        // from being recorded (or make a background mission appear stuck).
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();

            var missionId = TryGetGuid(payload, "MissionId") ?? Guid.Empty;
            var taskId = TryGetGuid(payload, "TaskId") ?? TryGetGuid(payload, "Id");
            var agentId = TryGetGuid(payload, "AgentId") ?? TryGetGuid(payload, "Id");

            var evt = ExecutionEvent.Record(missionId, taskId, agentId, eventType, JsonSerializer.Serialize(payload));
            db.ExecutionEvents.Add(evt);
            await db.SaveChangesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Replay persistence should be observable. The orchestration engine
            // remains responsible for deciding whether the mission itself failed.
            _logger.LogError(ex, "Failed to persist neural event {EventType} for replay.", eventType);
        }

        try
        {
            await _hub.Clients.Group(NeuralHub.BrainGroup).SendAsync("NeuralEvent", new
            {
                type = eventType,
                timestamp = DateTime.UtcNow,
                payload
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Live UI delivery is best-effort; a disconnected SignalR client
            // must not fail a mission that has already been persisted.
            _logger.LogWarning(ex, "Failed to broadcast neural event {EventType} over SignalR.", eventType);
        }
    }

    private static Guid? TryGetGuid(object payload, string propertyName)
    {
        var prop = payload.GetType().GetProperty(propertyName);
        if (prop?.GetValue(payload) is Guid g) return g;
        return null;
    }
}
