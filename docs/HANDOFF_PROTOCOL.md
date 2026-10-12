# Context handoff protocol

Handoffs are generated from real `AIResponse` usage/failure state. Simulation
is an explicitly labeled deterministic provider; it is not presented as a
real vendor response.

## Flow

1. **Monitor** — the orchestration engine evaluates normalized
   `AITokenUsage.UsageRatio` against configurable Warning/Critical/Exhaustion
   thresholds. A failed provider response can also request one fallback.
2. **Extract minimum state** — `ContextContinuityEngine` builds a structured
   `ContextHandoffPackage`; it does not copy a full conversation. The package
   carries mission/objective, current task/status, completed/remaining work,
   the previous agent's bounded output (marked untrusted), errors, and
   bounded reference material.
3. **Persist the package and handoff** — `IContextPackageSink` persists the
   package; `IHandoffSink` persists the handoff lifecycle as it transitions
   through `InProgress`, `Validating`, and `Completed` or `Rejected`.
4. **Validate** — `HandoffValidator` checks non-empty mission/task/source
   identifiers, mission/task/status fields, and remaining work. An
   insufficient package is rejected and the task fails with the missing
   fields. The current implementation does not automatically request
   missing fields from a previous provider.
5. **Continue once** — the selected fallback receives mission/task context,
   the structured package, trust-labeled references, and the previous output
   explicitly marked as data rather than instructions. Only one fallback
   attempt is allowed per task, preventing a retry/handoff loop.
6. **Record and observe** — each stage is stored as an `ExecutionEvent` and
   broadcast through an authenticated per-user SignalR group. The
   `Handoffs` and `Context` screens inspect the persisted records.

## Package outline

```json
{
  "mission": "Mission title and objective",
  "currentTask": "Task title",
  "status": "InProgress",
  "completedWork": ["Completed steps", "Untrusted prior-agent output"],
  "remainingWork": ["Continue and return a verifiable result"],
  "decisions": [],
  "constraints": [],
  "filesChanged": [],
  "errors": [],
  "tests": [],
  "dependencies": [],
  "openQuestions": [],
  "relevantMemory": ["Trust-labeled reference snippets"]
}
```

The current context extractor summarizes the tracked mission/task and the
last provider response; it does not yet retrieve long-term memory or inspect
repository/files automatically. Code execution is not part of an automatic
handoff.
