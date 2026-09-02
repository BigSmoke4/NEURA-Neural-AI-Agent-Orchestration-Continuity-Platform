namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Real-time neural activity events broadcast over SignalR. Every event
/// here corresponds to an actual backend state transition — never
/// fabricated (see "no fake functionality" acceptance rule).
/// </summary>
public interface INeuralEventPublisher
{
    Task PublishAsync(string eventType, object payload, CancellationToken ct = default);
}

public static class NeuralEventTypes
{
    public const string AgentConnected = nameof(AgentConnected);
    public const string AgentStarted = nameof(AgentStarted);
    public const string AgentThinking = nameof(AgentThinking);
    public const string AgentExecuting = nameof(AgentExecuting);
    public const string TokenUsageUpdated = nameof(TokenUsageUpdated);
    public const string ContextWarning = nameof(ContextWarning);
    public const string ContextCritical = nameof(ContextCritical);
    public const string ContextExhausted = nameof(ContextExhausted);
    public const string HandoffStarted = nameof(HandoffStarted);
    public const string HandoffProgress = nameof(HandoffProgress);
    public const string HandoffCompleted = nameof(HandoffCompleted);
    public const string TaskCreated = nameof(TaskCreated);
    public const string TaskStarted = nameof(TaskStarted);
    public const string TaskCompleted = nameof(TaskCompleted);
    public const string TaskFailed = nameof(TaskFailed);
    public const string MissionCompleted = nameof(MissionCompleted);
}
