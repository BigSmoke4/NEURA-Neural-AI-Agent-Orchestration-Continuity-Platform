# Architecture

NEURA is a **modular monolith**: one ASP.NET Core MVC host (`Web`)
referencing independently-namespaced modules under `Modules/`, each with
its own `Domain`/`Application`/(`Infrastructure`) folders. Modules only
depend on each other through interfaces/DTOs defined in `Application`,
never by reaching into another module's EF entities directly — see
`Handoff` depending on `ContextManagement.Domain` types rather than a
DbSet, for example.

## Module map

| Module | Responsibility | Status |
|---|---|---|
| AgentManagement | Agent entity, capabilities, scoring DTO | Implemented |
| ProviderIntegration | `IAIProvider`, Simulation + Anthropic adapters | Implemented (OpenAI/Google adapters follow the same interface — add as needed) |
| Orchestration | Mission/Task domain, `OrchestrationEngine`, `AgentRoutingEngine`, neural event contracts | Implemented (core loop) |
| ContextManagement | `ContextHandoffPackage`, threshold evaluation | Implemented |
| Handoff | `HandoffRecord`, `HandoffValidator` | Implemented |
| Memory | 5-type `MemoryRecord` domain | Domain only — CRUD/inspector UI not built |
| KnowledgeGraph | Node/edge domain | Domain only — graph UI not built |
| Observability | Health checks + Serilog wired in `Program.cs` | Partial — OpenTelemetry tracing exporters not configured |
| Web | MVC host, Brain dashboard, SignalR hub | Implemented for the Brain screen; Missions/Agents/Providers/etc. screens are nav placeholders |

## Data flow (the Section 96 diagram, concretely)

1. `BrainController.RunSimulationDemo` (or, in Real Mode, a background
   worker) creates a `Mission` and `AgentTask`.
2. `OrchestrationEngine.RunTaskAsync` calls `IAIProvider.ExecuteAsync` in
   a loop, publishing `TokenUsageUpdated` after every step.
3. `ContextEvaluator.Evaluate` classifies usage against
   `ContextThresholdOptions` (Warning 70% / Critical 85% / Exhaustion
   95%, all configurable).
4. On exhaustion, `ContextContinuityEngine.BuildPackage` produces a
   `ContextHandoffPackage` — structured, not a full transcript.
5. `HandoffValidator.Validate` checks the package is sufficient before
   `HandoffRecord` transitions to `Completed`.
6. The fallback agent/provider takes over, and the loop continues until
   the task completes.
7. Every step above publishes a `NeuralEvent` over SignalR
   (`INeuralEventPublisher` → `SignalRNeuralEventPublisher`), consumed by
   `wwwroot/js/dashboard/dashboard.js` to update the Cytoscape graph and
   the live event feed in real time.

## What would change at 100,000-user scale

- Split `NeuraDbContext` per bounded context and extract `Orchestration`
  + `ProviderIntegration` into a separate worker service consuming a
  queue (the module boundaries already make this a refactor, not a
  rewrite).
- Move `INeuralEventPublisher` to a durable pub/sub backbone (e.g. Redis
  backplane for SignalR, or a message bus) instead of in-process
  `IHubContext`.
- Aggregate `TokenUsageUpdated` telemetry server-side before broadcasting
  (per spec section 83) instead of one event per step.
