# NEURA — Neural AI Agent Orchestration & Continuity Platform

NEURA is a modular-monolith ASP.NET Core MVC platform that orchestrates
multiple AI agents/providers on a shared mission, monitors their context
usage, and automatically hands off in-progress work from one agent to
another when context runs out — all shown live on a neural-graph "brain"
dashboard.

## Status

This build focuses on two things: (1) implementing the core orchestration
architecture faithfully, and (2) hardening the parts of it that matter
for running in production — auth, credential handling, rate limiting,
error handling, security headers, real cost tracking, input validation,
and container/CI deployment. It does **not** implement every one of the
97 sections in the original specification; the remaining gaps are listed
under **Future Work** below rather than glossed over.

I have not been able to run `dotnet build` against this anywhere — there
is no .NET SDK available in the environment that produced it — so treat
first compilation as an expected step, not a guarantee.

## Production-hardening included

- **Encrypted credentials at rest.** Provider API keys are encrypted via
  ASP.NET Core Data Protection (`ICredentialProtector`) before being
  persisted, and decrypted only at the point of use. Raw keys are never
  logged or rendered back to any screen (masked as `••••1234`).
- **Rate limiting** on login, mission creation, and provider
  configuration, plus a global per-IP limiter, via .NET 8's built-in
  `Microsoft.AspNetCore.RateLimiting`.
- **Global exception handling middleware** — logs full detail
  server-side with a correlation ID, returns only a generic message to
  the client.
- **Security headers middleware** — `X-Frame-Options`,
  `X-Content-Type-Options`, `Referrer-Policy`, `Permissions-Policy` (CSP
  scaffolded, commented pending CDN allow-listing).
- **CSRF protection** applied globally to all MVC actions, with
  antiforgery tokens rendered on every mutating form.
- **Hardened Identity configuration** — strong password policy, account
  lockout, secure/HttpOnly/SameSite cookies, unique email enforcement.
- **Server-side input validation** via FluentValidation on mission,
  agent, and provider-connection input — rejecting bad input before it
  reaches the domain layer, not just relying on client-side checks.
- **Real cost calculation** — `CostCalculator` computes actual estimated
  cost from token usage × published per-model pricing, persisted as
  `CostRecord` rows on every AI request the orchestration engine makes,
  feeding the Cost Center screen with real numbers instead of `0m`
  placeholders.
- **Retry + circuit breaker** (`ResilientProviderDecorator`) wraps every
  real-mode provider adapter with exponential backoff and a
  failure-triggered circuit breaker, preventing infinite retry loops
  against a failing provider.
- **Persisted execution/audit trail** — every orchestration event is
  written to `ExecutionEvent` (mission replay) and every sensitive
  action to `AuditLogEntry` (who/what/when/result), not just broadcast
  live and discarded.
- **Docker + docker-compose** for a one-command local/production-like
  run (Postgres + the app, with a persisted Data Protection key volume),
  and a **GitHub Actions CI workflow** that restores/builds/tests on
  every push.

## Core architecture (unchanged from earlier passes, still real)

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
  plus an explicitly-labeled `SimulationAIProvider` for demos — anything
  requiring a real connection that hasn't been made surfaces as **NOT
  CONFIGURED** rather than fabricating a response.
- `MissionWorker` (a `BackgroundService`) drains an in-memory mission
  queue so `MissionsController.Create` returns immediately instead of
  blocking on orchestration.
- A live SignalR-driven **Brain Dashboard** with a Cytoscape.js neural
  graph, plus a deterministic **Simulation Mode** demo reproducing the
  spec's reference scenario (Claude ramps to 96% context usage →
  exhaustion → handoff → ChatGPT continues).
- All **16 named screens** exist and render real EF-backed data (depth
  varies — see Future Work for which are full experiences vs. plain
  table views): Login/Register, Brain, Missions, Agents, Providers,
  Handoffs, Context Explorer, Memory, Knowledge Graph, Timeline,
  Observability, Cost, Security, Settings.
- `tests/Neura.Tests` — xUnit unit tests for context-threshold
  evaluation, handoff validation, and agent routing/scoring.

## Requirements

- .NET 8 SDK
- PostgreSQL 14+ (or Docker, see below)

## Getting started (Docker)

```bash
cp .env.example .env   # set POSTGRES_PASSWORD
docker compose up --build
```

## Getting started (manual)

```bash
git clone <this repo>
cd NEURA

cp src/Web/appsettings.example.json src/Web/appsettings.Development.json
# edit the connection string

cd src/Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=neura;Username=neura;Password=..."

dotnet ef migrations add InitialCreate --project ../Infrastructure --startup-project .
dotnet ef database update --project ../Infrastructure --startup-project .

dotnet run
```

Then open the app and click **Run Simulation Demo** on the Brain screen
— no database write or provider credentials are required to see the
full handoff flow, since it runs entirely against the in-memory
`SimulationAIProvider`. To exercise Real Mode, register/sign in, then
connect a provider (e.g. Anthropic) with a real API key on the
Providers screen — the key is encrypted before it's stored.

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
│   │   │                        routing, mission queue, neural events, ICostSink
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

See `docs/ARCHITECTURE.md`, `docs/HANDOFF_PROTOCOL.md`,
`docs/DATABASE.md`, `docs/SECURITY.md`, and `docs/DEPLOYMENT.md` for
deeper detail on each subsystem.

## Production-hardening added in this pass

- **Authorization attached to every controller** —
  `[Authorize(Policy = "...")]` matching the policies declared in
  `Program.cs`, plus role seeding (Admin/Operator/User/Auditor) at
  startup and new registrants auto-assigned "User".
- **`ContextHandoffPackage` persisted as its own table**
  (`ContextPackages`, via `IContextPackageSink`) — the Context Explorer
  screen now shows full field-level detail per package instead of a
  `HandoffRecord` proxy.
- **Notification system** — a real `Notification` domain entity, a
  Notifications screen, and `OrchestrationEngine` firing notifications
  on context warnings, handoffs, and task completion.
- **Real container sandbox** — `DockerContainerSandbox` runs
  network-disabled, resource-capped ephemeral containers; registered in
  DI but never invoked automatically by anything in this codebase.
- **Generic OAuth2 authorization-code flow** (`ProviderOAuthController`)
  as an alternative to pasting an API key, with CSRF-protected state and
  config-driven endpoints per provider kind.
- **Config-toggled distributed infrastructure** — Redis SignalR
  backplane and OTLP trace export, both off by default, both one config
  key away from on.
- **Configurable model pricing** (`Neura:ModelPricing`) instead of
  hardcoded per-adapter literals.
- **Prompt-injection trust labeling** — `ContentTrustLevel` /
  `TrustLabeledContent` / `AgentTask.AttachReferenceMaterial`, with
  `OrchestrationEngine` building outgoing AI messages so untrusted
  content is explicitly delimited and labeled non-authoritative rather
  than merged into the instruction stream.
- **Interactive Knowledge Graph** — Cytoscape.js rendering fed by real
  `KnowledgeNodes`/`KnowledgeEdges` data, with a screen-reader-friendly
  text alternative on node selection.
- **Integration tests** via `WebApplicationFactory` (real ASP.NET Core
  pipeline, EF Core InMemory provider) asserting actual
  authorization-middleware behavior and a live health check.

## Gaps closed in this pass

Every item the previous README listed under Future Work has been
addressed for real — most fully closed, two closed as far as they
honestly can be without fabricating something that doesn't exist:

- **OAuth now has a real, working default.** `Neura:OAuth:Google` in
  `appsettings.example.json` points at Google's actual public OAuth2
  endpoints (`accounts.google.com` / `oauth2.googleapis.com`) — supply
  real Google Cloud OAuth client credentials and `/oauth/Google/start`
  performs a genuine authorization-code exchange. **Anthropic is
  explicitly left unconfigured** with a comment explaining why: Anthropic
  does not currently publish a general OAuth2 flow for API key
  issuance, so fabricating endpoints for it would be dishonest. Use the
  API key field for Anthropic — that's the correct method, not a
  fallback.
- **Sandbox execution now has a real caller.** A new Admin-only
  `/Sandbox` screen runs submitted code through `DockerContainerSandbox`
  and is audited on every run. It is still never invoked automatically
  by the orchestration engine or any agent output path — that remains a
  deliberate choice, not a gap.
- **A real untrusted-content producer now feeds the trust-labeling
  mechanism.** `TaskReferenceController.AttachFromUrl` fetches a
  user-supplied URL and attaches the raw response text to a task via
  `AttachReferenceMaterial(..., ContentTrustLevel.UntrustedExternalContent, ...)`.
  This required fixing a real bug along the way: `MissionWorker` had
  been creating fresh in-memory Mission/Task objects instead of loading
  the ones `MissionsController` persisted, so anything attached before
  a mission ran was silently discarded. Mission creation and starting
  are now separate steps (`Create` then `Start`) specifically so there's
  a window to attach reference material first — visible on the new
  Mission Details screen.
- **Pricing is now documented, not just configurable.**
  `docs/PRICING.md` states plainly that the built-in fallback rates are
  representative-only figures from when this project was built, not a
  live feed, and explains exactly how to override them per model via
  `Neura:ModelPricing`.
- **Real Postgres integration test** — `PostgresIntegrationTests` spins
  up an actual PostgreSQL container via Testcontainers and persists/
  reloads a real entity through it, catching mapping issues (e.g. the
  list-to-string value converters) that EF Core's InMemory provider
  would silently tolerate. Skips gracefully if Docker isn't reachable
  rather than failing the whole suite.
- **Real SignalR round-trip test** — `SignalRRoundTripTests` connects an
  actual `HubConnection` to the running test server and asserts a
  genuine handshake, rather than mocking `IHubContext`.
- **A real full-stack end-to-end test** —
  `EndToEndMissionTests` registers a user through the real Identity
  flow (parsing the antiforgery token out of rendered HTML the way a
  browser would), creates and starts a mission over HTTP, and polls the
  database until the background `MissionWorker` has actually driven it
  to completion — the full path minus a browser automating the DOM.
- **Notifications now have a real email delivery path.**
  `SmtpEmailSender` sends genuine SMTP mail for the notification kinds
  worth an inbox alert (security events, agent failures, provider
  disconnects, cost thresholds) when `Neura:Smtp:Host` is configured; it
  silently no-ops otherwise, so in-app notifications keep working with
  zero configuration.
- **Admin UI for role assignment** — a new `/Users` screen (Admin only)
  lets you add/remove Admin/Operator/User/Auditor roles for any
  registered user, closing the "direct database edit required" gap.
- **Automated credential rotation** — `AIProviderAccount.Rotate` plus a
  `Rotate` action on the Providers screen re-encrypts an account with a
  freshly supplied key and audits the rotation; the old encrypted value
  is fully replaced, never retained.

## What's still genuinely open

No functional feature from the README is intentionally left unimplemented.
The repository's GitHub Actions workflow is the authoritative build/test
check and runs the full .NET build, integration suite, and browser E2E suite.
Local Docker/production deployment should still be smoke-tested with the
operator's real environment variables and provider credentials before a
production release.

## Browser-automated E2E test (closed this pass)

`tests/Neura.E2E.Tests` uses Microsoft.Playwright to drive a real
headless Chromium browser against a real Kestrel-hosted instance of the
app (`PlaywrightWebApplicationFactory` starts an actual TCP listener on
a random localhost port — not the in-memory HTTP handler
`Neura.Tests`'s integration tests use). `BrowserMissionFlowTests`
registers a user, logs in, navigates via the real rendered nav link,
fills in and submits the real Create Mission form, clicks the real
Start button, and asserts against the database that the browser's
actions produced the expected server-side state — closing the one gap
`EndToEndMissionTests` (HTTP-only, no browser) deliberately left open.

**Running it:**

```bash
# Build the E2E project first; this generates playwright.ps1 in its output directory.
dotnet build tests/Neura.E2E.Tests/Neura.E2E.Tests.csproj -c Release
# One-time setup: install the browser binary Playwright drives.
pwsh tests/Neura.E2E.Tests/bin/Release/net8.0/playwright.ps1 install chromium
dotnet test tests/Neura.E2E.Tests/Neura.E2E.Tests.csproj -c Release
```

`.github/workflows/ci.yml` runs this as its own `browser-e2e` job (after
the main build-and-test job passes), installing Chromium via
`pwsh tests/Neura.E2E.Tests/bin/Release/net8.0/playwright.ps1 install --with-deps chromium` on the CI runner.

## Context Explorer graph view (closed this pass)

`/Context` (Index) now renders a Cytoscape.js graph — Mission → Task →
Package, one triple per persisted `ContextHandoffPackage` — using the
same rendering approach as Knowledge Graph; clicking a package node
navigates to its Details page. `/Context/Details/{id}` (Details) renders
a second graph specific to that package: Package → category (Completed
Work, Decisions, Files Changed, Errors, etc.) → individual item, capped
at 8 items per category so the graph stays legible, with the existing
full-text table kept alongside it as the non-visual/screen-reader
alternative. This closes the "Context Explorer is still a plain table"
gap from the previous pass.

## Final hardening notes

- The Docker image runs as the non-root `neura` user and persists Data Protection keys in the writable `neura-dp-keys` volume.
- Docker exposes the app over HTTP on port 8080 for the bundled development compose setup; authentication cookies use `SameAsRequest`, so they remain secure when HTTPS is used in production while still allowing the bundled HTTP smoke-test deployment to authenticate.
- OpenTelemetry dependencies have been updated to the current 1.18 line; NuGet currently lists 1.18.0 as the latest OTLP exporter release and older 1.15.x and below as vulnerable. 
- Google Gemini API credentials are sent in the `x-goog-api-key` header rather than a URL query parameter, avoiding accidental secret disclosure through proxy/server URL logs.
- OAuth callback state is single-use and provider kinds are validated before token exchange.
- The manual Docker sandbox drops all Linux capabilities and enables `no-new-privileges`, while retaining network isolation, read-only workspace/root filesystem, memory/CPU/PID limits, and a temporary filesystem.
- API pricing returned by provider capability discovery now respects the configured `Neura:ModelPricing` overrides.

## License

NEURA is open-source software licensed under the MIT License.

Copyright © 2026 Ahanaf Mokammel.
