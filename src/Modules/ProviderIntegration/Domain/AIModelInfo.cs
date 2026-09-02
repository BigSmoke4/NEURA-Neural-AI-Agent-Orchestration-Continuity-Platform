namespace Neura.Modules.ProviderIntegration.Domain;

public record AIModelCapabilities(
    string ModelId,
    int ContextWindowTokens,
    bool SupportsTools,
    bool SupportsVision,
    decimal InputCostPer1kTokens,
    decimal OutputCostPer1kTokens);

public record ProviderHealth(bool IsHealthy, string? Message, TimeSpan? Latency);

public record AIRequest(
    Guid AiRequestId,
    string ModelId,
    IReadOnlyList<(string Role, string Content)> Messages,
    int? MaxOutputTokens,
    IReadOnlyDictionary<string, string>? Metadata);

public record AITokenUsage(int InputTokens, int OutputTokens, int TotalTokens, int ContextWindow)
{
    public double UsageRatio => ContextWindow <= 0 ? 0 : (double)TotalTokens / ContextWindow;
}

public record AIResponse(
    Guid AiRequestId,
    string Content,
    AITokenUsage TokenUsage,
    TimeSpan Latency,
    decimal EstimatedCost,
    bool IsSuccess,
    string? ErrorMessage);
