using System.Threading.Channels;
using Neura.Modules.Orchestration.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Section 43: HTTP requests never block on long-running missions.
/// A mission is queued here and picked up by MissionWorker (a
/// BackgroundService in Web), which drives the OrchestrationEngine and
/// reports progress over SignalR. In-memory channel is sufficient for a
/// single-instance deployment; swap for a durable queue (e.g. a
/// Postgres-backed outbox or Redis stream) before scaling horizontally.
/// </summary>
public interface IMissionQueue
{
    ValueTask EnqueueAsync(QueuedMission mission, CancellationToken ct = default);
    IAsyncEnumerable<QueuedMission> DequeueAllAsync(CancellationToken ct);
}

public sealed record QueuedMission(Guid MissionId, string Title, string Objective, MissionMode Mode);

public sealed class InMemoryMissionQueue : IMissionQueue
{
    private readonly Channel<QueuedMission> _channel = Channel.CreateUnbounded<QueuedMission>();

    public async ValueTask EnqueueAsync(QueuedMission mission, CancellationToken ct = default)
        => await _channel.Writer.WriteAsync(mission, ct);

    public IAsyncEnumerable<QueuedMission> DequeueAllAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);
}
