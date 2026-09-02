namespace Neura.Modules.ProviderIntegration.Domain;

/// <summary>
/// Section 41: computes real estimated cost from a model's published
/// per-1k-token pricing (via AIModelCapabilities) and actual token usage,
/// instead of leaving EstimatedCost as a placeholder 0m.
/// </summary>
public static class CostCalculator
{
    public static decimal Estimate(AITokenUsage usage, AIModelCapabilities capabilities)
    {
        var inputCost = (usage.InputTokens / 1000m) * capabilities.InputCostPer1kTokens;
        var outputCost = (usage.OutputTokens / 1000m) * capabilities.OutputCostPer1kTokens;
        return Math.Round(inputCost + outputCost, 6);
    }
}
