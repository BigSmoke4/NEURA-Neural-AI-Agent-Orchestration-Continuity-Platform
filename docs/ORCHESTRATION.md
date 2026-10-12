# Orchestration

## Current execution path

Mission creation persists a mission and a starter task with status `Created`.
A separate queue action transitions the mission to `Queued`. `MissionWorker`
polls the database and atomically claims queued rows before invoking
`OrchestrationEngine` outside the HTTP request.

For Real Mode, the worker loads only the mission owner's connected providers
and enabled agents matching the task's declared capability. The deterministic
`AgentRoutingEngine` scores capability, relative context capacity,
reliability, connection health, cost profile, and latency profile. The worker
emits the selected agent and alternatives as an explainable routing event.

A successful provider response must have `IsSuccess=true`, a non-empty
output, and `IsTaskComplete=true` before the mission completes. Failed
responses are categorized without exposing raw upstream bodies. Context
exhaustion and provider failure can each request one validated fallback; the
engine will not recursively hand off forever. Provider cost/usage, output,
terminal task state, and neural events are recorded.

## Explicit limitations

- Current mission creation creates one starter task; a full AI planner and
  dependency-aware mission DAG execution are not implemented.
- Only one task is loaded/executed for a mission today; independent task
  parallelism and task-dependency scheduling remain future work.
- The routing engine is deterministic; there is no AI-assisted planner.
- Health scoring currently treats a connected provider account as healthy;
  active periodic provider probes and persisted health history are not yet
  wired into routing.
- Provider retries are bounded, but provider-wide circuit state is local to
  a provider instance. A distributed provider gateway/health store remains
  future work.
- `MissionRecoveryService` heartbeats active work and marks a `Running`
  mission `Failed` after its heartbeat expires. It also fails nonterminal
  tasks/handoffs and emits a persisted/broadcast `MissionFailed` event.
  Shutdown cancellation is stored as `Cancelled` when possible.
- Stale work is never automatically re-queued: provider-side effects may
  already have occurred, and the current provider contract cannot guarantee
  arbitrary request idempotency. Operators must inspect the timeline before
  creating a replacement mission. Checkpointed resume, operator retry UI,
  durable outbox delivery, and exactly-once external effects remain future
  work.
