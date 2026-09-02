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

    public PersistingNeuralEventPublisher(IHubContext<NeuralHub> hub, IServiceScopeFactory scopeFactory)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
    }

    public async Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
    {
        await _hub.Clients.Group(NeuralHub.BrainGroup).SendAsync("NeuralEvent", new
        {
            type = eventType,
            timestamp = DateTime.UtcNow,
            payload
        }, ct);

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
        catch
        {
            // Persisting the replay trail must never break the live orchestration flow.
            // A production build should log this failure via ILogger instead of swallowing it.
        }
    }

    private static Guid? TryGetGuid(object payload, string propertyName)
    {
        var prop = payload.GetType().GetProperty(propertyName);
        if (prop?.GetValue(payload) is Guid g) return g;
        return null;
    }
}
