using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Persists real per-request cost (section 41) as the orchestration
/// engine runs, rather than only estimating and discarding it.
/// </summary>
public interface ICostSink
{
    Task RecordAsync(ProviderKind provider, string modelId, int inputTokens, int outputTokens,
        decimal estimatedCost, Guid missionId, Guid taskId, Guid agentId, CancellationToken ct = default);
}
