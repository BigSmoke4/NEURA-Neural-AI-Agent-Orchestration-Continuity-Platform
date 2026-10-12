# NEURA verification status

**Updated: 2026-10-12.** This file describes the current workspace state; it
is not a release certification.

## Checks performed in this workspace

- Parsed both appsettings files as JSON, the Web/Infrastructure/test project
  files as XML, and ran `node --check` across the JavaScript files; all passed.
- Added integration coverage for account confirmation/password-reset tokens,
  uniform recovery responses, cache headers, account login/recovery rate-limit
  windows, heartbeat refresh, stale-run recovery, non-retry semantics,
  production configuration, database secret-file parsing, placeholder password
  rejection, and rendering persisted simulation output. The .NET tests are
  unrun here; PostgreSQL assertions require Docker.
- Reviewed configuration and deployment documentation against the current
  startup, queue, provider, and persistence paths.
- Enabled a same-origin script CSP, removed inline scripts/event attributes,
  self-hosted pinned Cytoscape and SignalR browser builds with license/hash
  records and SRI, fixed graph pages to load local Cytoscape, and moved graph
  JSON into HTML-encoded data attributes.
- Bounded the Knowledge Graph response to 500 nodes/1,000 edges and the live
  Brain graph to 800 recently active nodes; added an omission notice and
  controller coverage for the bounded knowledge payload.
- Added file-backed PostgreSQL/Data Protection/SMTP password support,
  loopback-only local Compose bindings, and a single-host production Compose
  example with required image/host/proxy/origin/mail settings and persistent
  volumes. Both Compose files parse as YAML; static checks confirm secret
  targets and port containment. Docker Compose config/runtime verification
  remains unrun because Docker is unavailable.
- Changed the PostgreSQL Testcontainers test to fail on container startup
  errors instead of silently returning a passing test without database assertions.
- Centralized the per-IP global and named quota definitions. Added a Redis
  Lua-backed distributed fixed-window limiter: global and endpoint counters
  update atomically, share a Redis Cluster hash slot, expire with their windows,
  and store only a digest of the normalized client IP. Redis failures fail closed
  with 503; liveness remains independent. Local ASP.NET Core limits remain the
  fallback when Redis is not configured. Middleware/fallback tests are added,
  but the Redis script has not been runtime-tested.
- Added production email confirmation/password reset, a canonical HTTPS origin,
  TLS SMTP validation, three-hour Identity tokens, and endpoint-specific per-IP
  recovery limits. The account-flow integration test uses a capturing sender
  and is not run here; SMTP still lacks a durable outbox.
- Added optional OTLP metrics for ASP.NET Core/Kestrel/HTTP-client meters,
  bounded-cardinality provider execution outcome/latency and successful
  token/context measurements, neural-event persistence, and SignalR counters/
  durations. Metric-tag tests are unrun. Task/handoff/database metrics and
  event/outbox atomicity remain unresolved.
- Ran tree-sitter syntax parsing for 80 changed/untracked C# files (including
  files beneath untracked directories) and `node --check` over all application
  and vendored JavaScript; all passed.
  `git diff --check` also passes. `npm audit` for the two pinned browser
  packages returned zero advisories on 2026-10-12. Added middleware and
  Playwright assertions for CSP behavior, but those .NET tests have not run.

## Not verified here

- No .NET build, Razor compilation, xUnit run, or Playwright run was
  performed: the .NET SDK is unavailable in this environment.
- No Docker image or Compose stack was built or exercised: Docker/Podman are
  unavailable here.
- PostgreSQL persistence/concurrency, Redis script atomicity/multi-instance
  quota behavior, and real provider requests were not runtime-tested here. CI
  and a deployment-environment smoke test remain release gates.

## Current operational caveats

- No reviewed EF migration baseline or model snapshot is checked in.
  Development/Testing use `EnsureCreatedAsync` only for disposable databases;
  Staging/Production fail fast until reviewed migrations are applied, and also
  require exact `AllowedHosts`, persistent Data Protection storage, an RSA
  private-key certificate, valid database credentials, canonical HTTPS origin,
  and TLS-enabled SMTP.
- Staging/Production require confirmed-email sign-in. Confirmation is POST-only
  after GET and reset tokens expire after three hours; reset responses are
  uniform, token pages are non-cacheable, and SMTP delivery is not queued durably.
- CSP blocks inline scripts, event attributes, and style blocks. Its
  `style-src-attr` directive still allows inline style attributes for legacy
  Razor and graph-library handling. Browser validation is required before
  removing that last inline-style allowance.
- Queued mission claims are conditional; active runs heartbeat `UpdatedAtUtc`
  and stale runs are terminally failed without auto-retry. This avoids a stuck
  `Running` row without risking an automatic duplicate provider side effect,
  but it is not checkpointed resume or exactly-once execution.
- Real provider execution requires an owner-configured official API key,
  enabled agent, matching capability, and an exact supported model ID.
  Provider OAuth remains disabled by default; refresh-token lifecycle and
  provider-specific OAuth credential semantics are incomplete.
- Mission mode is selected per mission. Simulation results are deterministic
  and explicitly labeled; they are not vendor responses or billable costs.
- Elevated registration role codes in the checked-in example are empty.
  Keep real secrets in a secret manager or environment, never Git.

## Fresh development database

The current model may not match a database created by an earlier revision.
For disposable local Compose data only, stop the stack and remove its
PostgreSQL volume before starting again:

```bash
docker compose down -v
docker compose up --build
```

**This removes all local database data. Never use `down -v` against a
production or otherwise retained database.** Establish and test reviewed EF
migrations before such a database is upgraded.
