namespace Neura.Modules.ProviderIntegration.Domain;

/// <summary>
/// Per-model pricing loaded at application startup from configuration
/// (Neura:ModelPricing). Operators can change rates without changing the
/// provider adapter code, but the application must be restarted for the
/// new values to take effect. Built-in fallback rates are illustrative;
/// verify rates with each provider before relying on cost estimates.
/// </summary>
public sealed class ModelPricingOptions
{
    public Dictionary<string, ModelPrice> Models { get; set; } = new();

    public ModelPrice GetOrDefault(string modelId, decimal fallbackInput, decimal fallbackOutput)
        => Models.TryGetValue(modelId, out var price) ? price : new ModelPrice { InputPer1k = fallbackInput, OutputPer1k = fallbackOutput };
}

public sealed class ModelPrice
{
    public decimal InputPer1k { get; set; }
    public decimal OutputPer1k { get; set; }
}
