using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Web.Observability;

namespace Neura.Web.Hubs;

/// <summary>
/// Persists neural events for replay and broadcasts them only to the owning
/// user plus the explicit operations/audit audience. A disconnected browser
/// cannot fail orchestration, and a tenant cannot observe another tenant's
/// mission payloads through the live event stream.
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
        var missionId = TryGetGuid(payload, "MissionId") ?? Guid.Empty;
        var ownerUserId = TryGetGuid(payload, "OwnerUserId");
        var persistenceStarted = Stopwatch.GetTimestamp();

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();

            if (!ownerUserId.HasValue && missionId != Guid.Empty)
            {
                ownerUserId = await db.Missions.AsNoTracking()
                    .Where(m => m.Id == missionId)
                    .Select(m => m.OwnerUserId)
                    .FirstOrDefaultAsync(ct);
            }

            var taskId = TryGetGuid(payload, "TaskId");
            var agentId = TryGetGuid(payload, "AgentId") ?? TryGetGuid(payload, "FromAgentId");
            var evt = ExecutionEvent.Record(missionId, taskId, agentId, eventType, JsonSerializer.Serialize(payload));
            db.ExecutionEvents.Add(evt);
            await db.SaveChangesAsync(ct);
            NeuraMetrics.RecordEventPersisted(eventType);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Replay persistence is a best-effort projection; the durable
            // mission/task state remains authoritative.
            NeuraMetrics.RecordEventPersistenceFailure(eventType);
            _logger.LogError(ex, "Failed to persist neural event {EventType} for replay.", eventType);
        }
        finally
        {
            NeuraMetrics.RecordEventPersistenceDuration(Stopwatch.GetElapsedTime(persistenceStarted).TotalMilliseconds);
        }

        var broadcastStarted = Stopwatch.GetTimestamp();
        NeuraMetrics.RecordSignalRBroadcastAttempt(eventType);
        try
        {
            if (ownerUserId.HasValue)
            {
                await _hub.Clients.Group(NeuralHub.UserGroup(ownerUserId.Value))
                    .SendAsync("NeuralEvent", CreateEnvelope(eventType, payload), ct);
            }

            // This group is only assigned to authenticated Admin/Auditor hub
            // connections in NeuralHub.OnConnectedAsync.
            await _hub.Clients.Group(NeuralHub.OperationsGroup)
                .SendAsync("NeuralEvent", CreateEnvelope(eventType, payload), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            NeuraMetrics.RecordSignalRBroadcastFailure(eventType);
            _logger.LogWarning(ex, "Failed to broadcast neural event {EventType} over SignalR.", eventType);
        }
        finally
        {
            NeuraMetrics.RecordSignalRBroadcastDuration(Stopwatch.GetElapsedTime(broadcastStarted).TotalMilliseconds);
        }
    }

    private static object CreateEnvelope(string eventType, object payload)
        => new { type = eventType, timestamp = DateTime.UtcNow, payload };

    private static Guid? TryGetGuid(object payload, string propertyName)
    {
        var property = payload.GetType().GetProperty(propertyName);
        if (property?.GetValue(payload) is Guid id)
            return id;
        return null;
    }
}
