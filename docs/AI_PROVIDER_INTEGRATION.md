# AI provider integration

`IAIProvider` separates vendor transport from orchestration. It exposes a
normalized `AIRequest`, `AIResponse`, `AIModelCapabilities`, and
`ProviderHealth`; provider-specific HTTP and telemetry mapping stays inside
the adapters.

## Supported adapters

- OpenAI Chat Completions
- Anthropic Messages
- Google Gemini `generateContent`
- `SimulationAIProvider` for deterministic demonstrations only

A provider account is explicitly connected by its owner. API keys are
Data-Protection encrypted before persistence. An Agent references a
provider account and holds the exact model ID, role, context capacity, and
capabilities. Real mission routing only considers that mission owner's
enabled agents and connected provider accounts. Unsupported kinds are
rejected rather than returning synthetic real-mode output.

## Normalized telemetry

Provider responses carry input/output/total token counts, estimated context
window, latency, estimated cost, success/error category, and a task-complete
signal. The orchestration layer evaluates context independently of vendor
response shape. Pricing may be overridden per model through
`Neura:ModelPricing`; shipped fallback prices are representative and must
be verified against the vendors' current published rates.

The current adapters still use conservative model/context-window defaults
rather than live model metadata discovery. `GetCapabilitiesAsync` is an
abstraction for future provider-backed discovery; the worker does not yet
perform a remote model-catalog synchronization.

## Authentication modes and boundaries

The supported path today is official API-key auth. OAuth authorization-code
scaffolding is disabled by default: a short-lived access token cannot be
used as a durable API key without provider-specific bearer semantics and
refresh-token rotation. No provider passwords, cookies, or consumer-site
sessions are accepted.

`ProviderFactory` decrypts the protected credential to instantiate the
adapter for a task. The current resilient decorator performs bounded retries
and a per-instance circuit check; a production gateway should make circuit
state durable/shared and apply provider-specific rate-limit headers.
