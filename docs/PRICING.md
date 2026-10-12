# Model pricing and cost records

NEURA records **estimated cost**, not a vendor invoice. A provider adapter
normalizes actual input/output token counts from a successful real API
response; `CostCalculator` multiplies those counts by the per-1,000-token
rates configured for the selected model. Simulation activity does not create
billable cost records.

## Configure rates

Set exact provider model IDs in `Neura:ModelPricing:Models` using app
configuration, environment variables, or a secret/configuration manager.
Rates below are dollars per 1,000 tokens.

Example schema (replace the model ID and both values with rates you have
verified directly with the provider; **the example numbers are illustrative,
not current vendor pricing**):

```json
{
  "Neura": {
    "ModelPricing": {
      "Models": {
        "replace-with-exact-model-id": {
          "InputPer1k": 0.001,
          "OutputPer1k": 0.002
        }
      }
    }
  }
}
```

The application binds pricing at startup, so restart the web process after
changing these values. A model without an explicit override uses the
adapter's built-in representative fallback rate (currently provider-wide,
not model-specific). Those fallback rates may be inaccurate for the selected
model, tier, cached input, batch discounts, or other billing adjustments.
Do not use them for budgeting, invoicing, or billing reconciliation.

Provider APIs can change both prices and token accounting. Check the
provider's current published pricing and actual account usage before
relying on an estimate. A future improvement is to attach rate provenance,
pricing version, cache/batch token categories, and reconciliation status to
each cost record; those are not currently stored.
