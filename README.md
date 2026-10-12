# NEURA — Neural AI Agent Orchestration & Continuity Platform

NEURA is a modular-monolith ASP.NET Core MVC platform that orchestrates
multiple AI agents/providers on a shared mission, monitors their context
usage, and hands off in-progress work to one eligible fallback when the
configured context-exhaustion threshold is reached — all shown live on a
neural-graph "brain" dashboard.

## Status

The repository contains a substantial working foundation, but it is not
an implementation of every one of the 97 specification sections and should
not be represented as production-ready yet. This pass corrected several
end-to-end gaps: real provider output is now validated and surfaced instead
of fabricated completion, handoffs are persisted, queued missions survive
process restarts, provider/agent ownership is enforced, event delivery is
tenant-scoped, and URL ingestion is hardened against SSRF.

## Security and operational foundations

- **Encrypted credentials at rest.** Provider API keys are encrypted via
  ASP.NET Core Data Protection (`ICredentialProtector`) before being
  persisted, and decrypted only at the point of use. Raw keys are never
  logged or rendered back to any screen; there is no masked-key display.
- **Per-IP rate limiting** on login/registration, account recovery, mission
  creation, and provider configuration, plus a global IP limit. Without Redis,
  ASP.NET Core enforces windows per process; setting
  `Neura:Redis:ConnectionString` switches both global and named windows to
  atomic shared Redis counters.
- **Global exception handling middleware** — logs full detail
  server-side with a correlation ID, returns only a generic message to
  the client.
- **Security headers middleware** — `X-Frame-Options`,
  `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy`, and an
  enforced same-origin script CSP. Cytoscape and SignalR are pinned and
  self-hosted; inline scripts/event handlers and inline style blocks are
  blocked. Inline style attributes remain temporarily allowed for legacy
  Razor styles and graph rendering.
- **CSRF protection** applied globally to all MVC actions, with
  antiforgery tokens rendered on every mutating form.
- **Hardened Identity configuration** — strong password policy, account
  lockout, secure/HttpOnly/SameSite cookies, unique email enforcement.
- **Server-side input validation** via FluentValidation on mission,
  agent, and provider-connection input — rejecting bad input before it
  reaches the domain layer, not just relying on client-side checks.
- **Real cost calculation** — `CostCalculator` estimates per-model cost
  from normalized token usage and configured pricing; successful real-mode
  provider responses are persisted as `CostRecord` rows. Simulation is
  excluded from billable cost records.
- **Retry + circuit breaker** (`ResilientProviderDecorator`) wraps every
  real-mode provider adapter with exponential backoff and a
  failure-triggered circuit breaker, preventing infinite retry loops
  against a failing provider.
- **Persisted execution/audit trail** — orchestration events are written
  to `ExecutionEvent` for mission replay when the event projection succeeds;
  persistence and SignalR broadcast are not atomic, and write failures are
  logged. Sensitive UI actions use `AuditLogEntry` (who/what/when/result).
- **Docker Compose** for a one-command local run (PostgreSQL + the app,
  with persisted database and Data Protection key volumes), and a **GitHub
  Actions CI workflow** on pushes to `main` and pull requests targeting
  `main`.

## Core architecture foundation

- Modular monolith matching the spec's module map: AgentManagement,
  ProviderIntegration, Orchestration, ContextManagement, Handoff,
  Memory, KnowledgeGraph, Observability, Shared, Infrastructure, Web.
- `OrchestrationEngine` runs a task against a provider, monitors
  token/context usage every step, and auto-triggers a handoff at a
  configurable exhaustion threshold.
- `ContextContinuityEngine` builds a structured `ContextHandoffPackage`
  (not a raw transcript); `HandoffValidator` checks it's sufficient
  before the receiving agent is allowed to continue.
- `AgentRoutingEngine` produces an explainable, weighted score per
  candidate agent rather than a black-box choice.
- Real provider adapters for **Anthropic, OpenAI, and Google** calling
  each vendor's official API (never scraping, never storing passwords),
  plus an explicitly-labeled `SimulationAIProvider` for demos — a Real
  mission without a matching connected account and enabled agent fails
  clearly rather than falling back to simulated output.
- `MissionWorker` (a `BackgroundService`) polls persisted `Queued`
  missions, uses a conditional database claim to prevent duplicate work
  across instances, and runs orchestration outside the HTTP request.
- A live SignalR-driven **Brain Dashboard** with a Cytoscape.js neural
  graph, plus a deterministic **Simulation Mode** demo reproducing the
  spec's reference scenario (Claude ramps to 96% context usage →
  exhaustion → handoff → ChatGPT continues).
- MVC views cover account access, Brain, Missions, Agents, Providers,
  Handoffs, Context Explorer, Memory, Knowledge Graph, Timeline,
  Observability, Cost, Security, Settings, Notifications, Users, and the
  manual Sandbox. Most operational records come from EF-backed data;
  depth and workflow coverage vary, as detailed in the subsystem docs.
- `tests/Neura.Tests` — xUnit unit and ASP.NET integration tests cover
  routing, context thresholds, trust labels, handoff validation/persistence,
  authenticated and owner-scoped SignalR delivery, resource-scope queries,
  HTTP health, and a deterministic worker-driven mission flow. A separate Testcontainers test exercises
  PostgreSQL when Docker is available. These tests have not been run in
  this workspace.

## Requirements

- .NET 8 SDK
- PostgreSQL 14+ (or Docker, see below)

## Getting started (Docker)

```bash
cp .env.example .env
mkdir -p secrets
openssl rand -base64 32 > secrets/postgres_password
chmod 644 secrets/postgres_password # local dev only; readable by the non-root container UID
docker compose up --build
```

## Getting started (manual)

```bash
cp src/Web/appsettings.example.json src/Web/appsettings.Development.json
dotnet user-secrets init --project src/Web/Web.csproj
dotnet user-secrets set \
  "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=neura;Username=neura;Password=..." \
  --project src/Web/Web.csproj
dotnet run --project src/Web/Web.csproj
```

A fresh local database is provisioned from the current EF model at startup.
**There is no checked-in migration baseline**, so this does not upgrade an
existing schema; use a disposable dev database and create/review migrations
before production data is retained. The Brain's Simulation Demo persists a
mission, output, execution events, context package, and handoff while clearly
labeling all simulator activity. To use Real Mode, sign in, connect your own
official provider API key, create an agent with a real model ID and matching
capability, then create and queue a Real mission.

## Project layout

```
NEURA/
├── src/
│   ├── Web/                     ASP.NET Core MVC host, Razor views, SignalR hub,
│   │                            middleware, validators, wwwroot
│   ├── Modules/
│   │   ├── AgentManagement/     Agent entity, capabilities, scoring types
│   │   ├── ProviderIntegration/ IAIProvider abstraction, Simulation/Anthropic/
│   │   │                        OpenAI/Google adapters, resilience decorator,
│   │   │                        cost calculator
│   │   ├── Orchestration/       Mission/Task domain, OrchestrationEngine,
│   │   │                        routing, continuity, neural event contracts
│   │   ├── ContextManagement/   Context handoff package, threshold evaluation
│   │   ├── Handoff/             Handoff record, validation
│   │   ├── Memory/              Memory record domain (5 memory types)
│   │   ├── KnowledgeGraph/      Knowledge node/edge domain
│   │   └── Observability/       Audit log + execution event (replay) domain
│   ├── Shared/                  Entity base class, Result, CorrelationContext, Clock
│   └── Infrastructure/          EF Core DbContext, Identity, credential encryption,
│                                 audit/cost EF sinks
├── tests/Neura.Tests/           xUnit unit tests
├── docs/                        Architecture & protocol docs
├── Dockerfile, docker-compose.yml
└── .github/workflows/ci.yml
```

See `docs/ARCHITECTURE.md`, `docs/ORCHESTRATION.md`,
`docs/AI_PROVIDER_INTEGRATION.md`, `docs/HANDOFF_PROTOCOL.md`,
`docs/MEMORY_ARCHITECTURE.md`, `docs/KNOWLEDGE_GRAPH.md`,
`docs/DATABASE.md`, `docs/SECURITY.md`, `docs/OBSERVABILITY.md`,
`docs/PRICING.md`, `docs/DEVELOPMENT.md`, and `docs/DEPLOYMENT.md` for
subsystem details, operational steps, and explicit limitations.

## Security and persistence updates

- Controllers enforce server-side authorization policies. New users receive
  only the `User` role; privileged registration codes have no shipped
  default. Auditors have read-only cross-user access and cannot queue/run
  missions, delete memory, or mutate notifications. Provider keys and agent
  records are owner-scoped; operational access is policy-scoped.
- Staging/Production startup fails closed unless host filtering is explicit,
  the Data Protection key path and RSA private-key certificate are configured
  with absolute paths, and the database has a complete applied EF migration
  history. `docker-compose.yml` runs in Development; a separate
  `docker-compose.production.yml` example is documented but does not bypass
  migration, image, secret, backup, and environment-level release gates.
- The authenticated SignalR hub routes mission activity to a per-user group;
  Admin/Auditor users join a separate operations group. Event text is
  inserted into the dashboard with `textContent`, not `innerHTML`.
- Handoffs persist both `ContextHandoffPackage` and each lifecycle state via
  `IHandoffSink`. Provider results are checked before a task can be marked
  complete; raw upstream error bodies are not sent to the browser.
- The hosted worker claims persisted `Queued` missions with a conditional
  database update. The in-memory channel has been removed.
- URL reference ingestion is limited to public HTTP/HTTPS destinations,
  standard ports, no redirects, capped response size/time, and a
  connection-time public-IP check to resist SSRF.
- **OAuth is intentionally disabled by default.** A generic callback alone
  is not sufficient to handle provider-specific scopes, bearer-vs-API-key
  auth, and refresh-token rotation. Use official API keys until those
  lifecycles are implemented.
- Docker, Redis SignalR backplane/session cache, OpenTelemetry OTLP export,
  SMTP, and the manual sandbox are deployment-configured features. SMTP is
  required in Staging/Production for verified-account sign-in and recovery;
  the other integrations remain optional. The sandbox is never called
  automatically by model output.

## Known gaps — this is not a production release yet

- The generated `InitialBaseline` EF migration and model snapshot are checked
  in. CI verifies snapshot parity and applies it to a fresh PostgreSQL
  Testcontainer. Development/Testing still use `EnsureCreated` only for
  disposable databases; retained schemas created that way require a separately
  reviewed adoption plan, and production backup/upgrade/rollback rehearsal
  remains necessary.
- Mission creation persists one starter task. Full AI mission decomposition,
  task DAG scheduling/parallelism, semantic memory search/decay, automatic
  knowledge-graph ingestion, and reviewed model-output fact extraction are
  not implemented. The project Memory Center supports scoped create/revise/
  search/delete, bounded lexical retrieval, explicit untrusted labeling,
  retention, and background cleanup.
- Active missions heartbeat their persisted `UpdatedAtUtc`. Stale runs are
  marked `Failed` after the configurable timeout and are never auto-requeued,
  because a provider-side effect may already have occurred. This prevents
  permanently stuck `Running` rows, but checkpointed resume, operator retry
  UI, and exactly-once provider effects are not implemented.
- Routing is deterministic and explainable, but provider health is currently
  inferred from a connected account rather than active health probes.
- Provider/event metrics can export through OTLP, but dashboards, retention,
  and several domain metrics are incomplete. The CSP's temporary inline-style
  allowance remains. Durable email is encrypted and retried through a
  PostgreSQL outbox; delivery is at-least-once, with dead-letter admin/replay
  and sent-row retention tooling still absent. Redis-backed quotas are verified
  across independent clients and fail closed during outage; deployment-specific
  Sentinel/Cluster failover, ACL/TLS, and network policy still need smoke tests.
- GitHub Actions verifies the .NET build, unit/integration tests, PostgreSQL
  migrations, Redis outage/recovery gates, and browser E2E. This sandbox has no
  local .NET SDK or Docker daemon; production backup/restore and deployment
  topology smoke tests remain release tasks.

## Browser-automated E2E test

`tests/Neura.E2E.Tests` uses Microsoft.Playwright to drive headless
Chromium against an already-running NEURA instance. The test registers a
user, follows the rendered navigation, creates a Simulation mission, checks
the mission details rendered in the browser, and queues the mission. It verifies
the rendered HTTP flow; it does not inspect database rows, assert that the
background worker completed, or exercise real provider calls. The separate
`EndToEndMissionTests` in `tests/Neura.Tests` polls the test database for a
persisted `TaskCompleted` event after the worker runs the deterministic
Simulation provider.

**Running the browser test:**

```bash
# Build and install Playwright's Chromium browser once.
dotnet build tests/Neura.E2E.Tests/Neura.E2E.Tests.csproj -c Release
pwsh tests/Neura.E2E.Tests/bin/Release/net8.0/playwright.ps1 install chromium

# Start NEURA separately in another terminal, then:
NEURA_E2E_BASE_URL=http://127.0.0.1:5080 \
  dotnet test tests/Neura.E2E.Tests/Neura.E2E.Tests.csproj -c Release
```

The CI workflow starts the app on the runner and runs the browser test in a
separate `browser-e2e` job after the main build-and-test job passes. Neither
the browser test nor the simulation worker proves production-provider
correctness.

## Context Explorer graph view

`/Context` (Index) now renders a Cytoscape.js graph — Mission → Task →
Package, one triple per persisted `ContextHandoffPackage` — using the
same rendering approach as Knowledge Graph; clicking a package node
navigates to its Details page. `/Context/Details/{id}` (Details) renders
a second graph specific to that package: Package → category (Completed
Work, Decisions, Files Changed, Errors, etc.) → individual item, capped
at 8 items per category so the graph stays legible, with the existing
full-text table kept alongside it as the non-visual/screen-reader
alternative.

## Final hardening notes

- The Docker image runs as non-root UID 10001 (`neura`). Compose mounts the Data Protection key ring at `/app/dp-keys`; local file secrets are development-only, while the production example uses file-backed database and certificate secrets.
- The development Compose profile binds HTTP and PostgreSQL to loopback on ports 8081/5433 and explicitly uses `SameAsRequest` cookies. The production example binds the app to loopback behind a trusted HTTPS proxy, requires exact host/proxy settings, and uses `Always` cookies.
- Google Gemini API credentials are sent in the `x-goog-api-key` header rather than a URL query parameter.
- OAuth callback state is single-use and provider kinds are validated, but OAuth remains disabled until provider-specific token and refresh behavior is implemented.
- The manual Docker sandbox drops Linux capabilities and enables `no-new-privileges`, with network isolation, read-only root filesystem, resource limits, and temporary storage.
- Model pricing is an operator-supplied estimate; built-in fallback rates are illustrative and are not live vendor pricing.

## License

NEURA is open-source software licensed under the MIT License.

Copyright © 2026 Ahanaf Mokammel.
