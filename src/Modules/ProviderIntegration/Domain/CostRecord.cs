namespace Neura.Modules.ProviderIntegration.Domain;

/// <summary>Section 41: per-request cost tracking for the Cost Center screen.</summary>
public class CostRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public ProviderKind Provider { get; private set; }
    public string ModelId { get; private set; } = default!;
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal EstimatedCost { get; private set; }
    public Guid? MissionId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? AgentId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private CostRecord() { }

    public static CostRecord Record(ProviderKind provider, string modelId, int inputTokens, int outputTokens,
        decimal estimatedCost, Guid? missionId, Guid? taskId, Guid? agentId)
        => new()
        {
            Provider = provider,
            ModelId = modelId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            EstimatedCost = estimatedCost,
            MissionId = missionId,
            TaskId = taskId,
            AgentId = agentId
        };
}
