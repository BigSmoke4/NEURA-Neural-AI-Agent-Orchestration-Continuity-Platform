# Handoff Protocol

1. **Detect** — `OrchestrationEngine` observes `AITokenUsage.UsageRatio`
   crossing `ContextThresholdOptions.ExhaustionThreshold`.
2. **Extract** — `ContextContinuityEngine.BuildPackage` produces a
   `ContextHandoffPackage` with: mission, current task, status, completed
   work, remaining work, decisions, constraints, files changed, errors,
   tests, dependencies, open questions, relevant memory.
3. **Create handoff record** — `HandoffRecord.Create` with a
   `HandoffReason` (ContextExhaustion, ProviderFailure, AgentFailure,
   CapabilityMismatch, CostOptimization, LatencyOptimization,
   LowConfidence, ManualTransfer, PolicyRestriction, RateLimit, Timeout).
4. **Transfer** — `HandoffRecord.BeginTransfer()` → assign receiving
   agent via `AssignReceiver`.
5. **Validate** — `HandoffValidator.Validate` checks the package for
   missing required fields (`currentTask`, `status`, `remainingWork`).
   If insufficient, the orchestrator's contract is to request only the
   missing pieces (`REQUEST_ADDITIONAL_CONTEXT`) rather than fail
   outright — the current reference loop fails the task and surfaces the
   missing fields; wiring the retry-with-partial-context path is a
   natural next extension point.
6. **Continue** — the receiving agent resumes execution; every stage
   publishes a `NeuralEvent` (`HandoffStarted`, `HandoffProgress`,
   `HandoffCompleted`) for the live dashboard.
