# Model Pricing

`Neura:ModelPricing` in configuration drives `ModelPricingOptions`,
consumed by `CostCalculator` via each provider adapter. The defaults
compiled into the adapters (used only when a model isn't listed in
config) reflect roughly representative per-1k-token rates for
mid-tier models from Anthropic, OpenAI, and Google **as published
around early-to-mid 2025** — sourced from general knowledge at the time
this project was built, not fetched live. AI vendor pricing changes
frequently and varies by exact model tier (e.g. Opus vs Sonnet vs
Haiku-class models differ by 5-10x), so:

1. **Before trusting Cost Center numbers for anything real**, look up
   the current published pricing page for each vendor/model you've
   actually connected, and set it explicitly under
   `Neura:ModelPricing:Models:{exact-model-id}` in configuration —
   config always overrides the adapter's built-in fallback.
2. Treat the built-in fallback values purely as "something reasonable
   renders in the UI during development," never as a billing source of
   truth.
3. If you need pricing for a model not listed here, add it to config —
   no code change or redeploy required, since `ProviderFactory` reads
   `ModelPricingOptions` at request time.

Example:

```json
"Neura": {
  "ModelPricing": {
    "Models": {
      "claude-opus-5": { "InputPer1k": 0.015, "OutputPer1k": 0.075 },
      "claude-sonnet-5": { "InputPer1k": 0.003, "OutputPer1k": 0.015 },
      "gpt-5": { "InputPer1k": 0.005, "OutputPer1k": 0.015 }
    }
  }
}
```
