# Observability

## Persisted operational history

`PersistingNeuralEventPublisher` records orchestration transitions in
`ExecutionEvents` and broadcasts them over authenticated SignalR. Events
carry mission/task/agent identifiers, timestamp, type, and structured JSON
payload; owner IDs route live notifications to the correct user group. The
Timeline screen replays persisted mission events. `MissionFailed` identifies
stale-run recovery and explicitly includes `RecoveryAction: NotRetried`;
`MissionCancelled` reports graceful host cancellation. These events reflect
persisted state decisions, not inferred provider outcomes.

Sensitive UI actions use `IAuditLogger` / `AuditLogs`. Provider costs and
normalized token counts are recorded by `ICostSink` / `CostRecords` for real
provider calls; Simulation Mode is explicitly excluded from billable cost
records. Notifications are persisted separately.

## Tracing and health

Serilog writes structured application logs. OpenTelemetry instruments
ASP.NET Core and outbound `HttpClient` activity, and exports the framework
hosting/Kestrel/HTTP-client meters plus custom, bounded-cardinality provider
execution outcome/latency, successful token-usage/context-ratio, persisted-event,
and SignalR broadcast counters/durations. Provider labels use a fixed allow-list;
model, mission, task, owner, prompt, and credential values are excluded.
Simulation calls are tagged `neura.mode=simulation` and `provider.name=simulation`.
OTLP export is optional through `Neura:Observability:OtlpEndpoint`;
without it, measurements are not shipped to a collector. Metric dimensions use
fixed allow-lists for event types, provider names, execution mode, and outcomes;
mission, task, owner, model, prompt, and credential values are excluded.
`/health/live` is process liveness, `/health/ready` includes PostgreSQL, and
`/health` reports the full health-check set.

## Dashboard scope

The Brain's connection indicator reflects the actual SignalR lifecycle;
agent/task/handoff counters reflect events observed since page load. Token
usage is the most recently received provider/simulation response, and the
cost value is an estimate accumulated from received real-provider events on
that page. The live Brain graph prunes least-recently-active nodes above 800,
and the text event feed retains at most 200 items to bound browser memory.
CPU is marked uninstrumented and database readiness is marked unchecked
there; neither is displayed as synthetic telemetry.

## Gaps

The custom metrics remain partial: task duration, handoff latency, database
latency, SignalR connection counts, and provider retry-level details are not
yet exported. Provider latency covers the orchestration call including any
configured retries; token counters and context ratios are recorded only for
successful responses. Event persistence is best effort and has no outbox for
persistence/broadcast atomicity; a database failure can leave a live update
without a replay row. Retention policies, comprehensive log redaction rules
beyond avoiding credential logging, and a metrics/trace visualization backend
are also absent. Configure an OTLP collector and structured log sink for
deployments, then add dashboards and alerting before production use.
