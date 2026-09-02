namespace Neura.Modules.ProviderIntegration.Domain;

/// <summary>
/// Per-model pricing loaded from configuration (Neura:ModelPricing) so
/// operators can update rates as vendors change them, without a code
/// change or redeploy of the adapters themselves. Figures shipped in
/// appsettings.json are illustrative placeholders — replace with each
/// vendor's current published pricing before trusting Cost Center
/// numbers; see docs/DATABASE.md and README Future Work.
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
