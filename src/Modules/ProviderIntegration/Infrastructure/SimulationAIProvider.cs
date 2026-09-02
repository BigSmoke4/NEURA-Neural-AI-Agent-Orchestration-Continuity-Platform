using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Deterministic, explicitly-labeled simulation provider used for demo mode
/// (see acceptance scenario: predictable context-usage ramp -> handoff).
/// This is never presented as a real provider connection.
/// </summary>
public sealed class SimulationAIProvider : IAIProvider
{
    private static readonly int[] UsageRampPercent = { 30, 55, 72, 88, 96 };
    private int _step;
    private readonly string _personaName;
    private readonly int _contextWindow;

    public SimulationAIProvider(string personaName, int contextWindow = 100)
    {
        _personaName = personaName;
        _contextWindow = contextWindow;
    }

    public ProviderKind Kind => ProviderKind.Simulation;
    public bool IsSimulation => true;

    public Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken)
    {
        var percent = UsageRampPercent[Math.Min(_step, UsageRampPercent.Length - 1)];
        _step++;

        var totalTokens = (int)Math.Round(_contextWindow * (percent / 100.0));
        var usage = new AITokenUsage(
            InputTokens: (int)(totalTokens * 0.7),
            OutputTokens: (int)(totalTokens * 0.3),
            TotalTokens: totalTokens,
            ContextWindow: _contextWindow);

        var response = new AIResponse(
            AiRequestId: request.AiRequestId,
            Content: $"[SIMULATION:{_personaName}] processed step {_step} at {percent}% context usage.",
            TokenUsage: usage,
            Latency: TimeSpan.FromMilliseconds(180 + Random.Shared.Next(0, 400)),
            EstimatedCost: 0m,
            IsSuccess: true,
            ErrorMessage: null);

        return Task.FromResult(response);
    }

    public Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken)
        => Task.FromResult(new AIModelCapabilities(modelId, _contextWindow, true, false, 0m, 0m));

    public Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(new ProviderHealth(true, "Simulation provider always healthy", TimeSpan.FromMilliseconds(5)));

    public void Reset() => _step = 0;
}
