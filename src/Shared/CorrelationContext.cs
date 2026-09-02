namespace Neura.Shared;

/// <summary>
/// Correlation identifiers propagated through orchestration, execution,
/// handoff and AI request pipelines for structured logging and tracing.
/// </summary>
public sealed record CorrelationContext(
    Guid CorrelationId,
    Guid? MissionId = null,
    Guid? TaskId = null,
    Guid? AgentId = null,
    Guid? ProviderId = null,
    Guid? ExecutionId = null,
    Guid? HandoffId = null,
    Guid? AiRequestId = null)
{
    public static CorrelationContext New() => new(Guid.NewGuid());
}
