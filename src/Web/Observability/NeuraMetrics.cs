using System.Diagnostics.Metrics;
using Neura.Modules.Orchestration.Application;

namespace Neura.Web.Observability;

/// <summary>
/// Low-cardinality operational measurements for the web host. Event names are
/// allow-listed so a malformed or future caller cannot turn user data into
/// metric dimensions. Never attach mission, task, owner, prompt, or credential
/// values to metrics.
/// </summary>
public static class NeuraMetrics
{
    public const string MeterName = "Neura.Web";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> PersistedEvents =
        Meter.CreateCounter<long>("neura.events.persisted", "{event}");
    private static readonly Counter<long> EventPersistenceFailures =
        Meter.CreateCounter<long>("neura.events.persistence.failures", "{event}");
    private static readonly Histogram<double> EventPersistenceDuration =
        Meter.CreateHistogram<double>("neura.events.persistence.duration", "ms");
    private static readonly Counter<long> SignalRBroadcastAttempts =
        Meter.CreateCounter<long>("neura.signalr.broadcast.attempts", "{event}");
    private static readonly Counter<long> SignalRBroadcastFailures =
        Meter.CreateCounter<long>("neura.signalr.broadcast.failures", "{event}");
    private static readonly Histogram<double> SignalRBroadcastDuration =
        Meter.CreateHistogram<double>("neura.signalr.broadcast.duration", "ms");

    public static void RecordEventPersisted(string eventType)
        => PersistedEvents.Add(1, EventTypeTag(eventType));

    public static void RecordEventPersistenceFailure(string eventType)
        => EventPersistenceFailures.Add(1, EventTypeTag(eventType));

    public static void RecordEventPersistenceDuration(double milliseconds)
        => EventPersistenceDuration.Record(milliseconds);

    public static void RecordSignalRBroadcastAttempt(string eventType)
        => SignalRBroadcastAttempts.Add(1, EventTypeTag(eventType));

    public static void RecordSignalRBroadcastFailure(string eventType)
        => SignalRBroadcastFailures.Add(1, EventTypeTag(eventType));

    public static void RecordSignalRBroadcastDuration(double milliseconds)
        => SignalRBroadcastDuration.Record(milliseconds);

    private static KeyValuePair<string, object?> EventTypeTag(string eventType)
        => new("event.type", IsKnownEventType(eventType) ? eventType : "other");

    private static bool IsKnownEventType(string eventType)
        => eventType is
            NeuralEventTypes.AgentConnected or
            NeuralEventTypes.AgentStarted or
            NeuralEventTypes.AgentThinking or
            NeuralEventTypes.AgentExecuting or
            NeuralEventTypes.TokenUsageUpdated or
            NeuralEventTypes.ContextWarning or
            NeuralEventTypes.ContextCritical or
            NeuralEventTypes.ContextExhausted or
            NeuralEventTypes.HandoffStarted or
            NeuralEventTypes.HandoffProgress or
            NeuralEventTypes.HandoffCompleted or
            NeuralEventTypes.TaskCreated or
            NeuralEventTypes.TaskStarted or
            NeuralEventTypes.TaskCompleted or
            NeuralEventTypes.TaskFailed or
            NeuralEventTypes.MissionCompleted or
            NeuralEventTypes.MissionFailed or
            NeuralEventTypes.MissionCancelled or
            NeuralEventTypes.ProjectMemoryRetrieved or
            NeuralEventTypes.ProviderFailure or
            NeuralEventTypes.RoutingDecision;
}
