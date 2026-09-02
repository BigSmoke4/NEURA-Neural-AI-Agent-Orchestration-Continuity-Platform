namespace Neura.Modules.Observability.Domain;

/// <summary>
/// Persisted timeline entry (section 32/78): every NeuralEvent published
/// during orchestration is also written here so completed missions can
/// be replayed after the fact, not only watched live over SignalR.
/// </summary>
public class ExecutionEvent
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MissionId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? AgentId { get; private set; }
    public string EventType { get; private set; } = default!;
    public string PayloadJson { get; private set; } = default!;
    public DateTime TimestampUtc { get; private set; } = DateTime.UtcNow;

    private ExecutionEvent() { }

    public static ExecutionEvent Record(Guid missionId, Guid? taskId, Guid? agentId, string eventType, string payloadJson)
        => new() { MissionId = missionId, TaskId = taskId, AgentId = agentId, EventType = eventType, PayloadJson = payloadJson };
}
