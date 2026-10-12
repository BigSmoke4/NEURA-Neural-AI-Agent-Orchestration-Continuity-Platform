# Database

NEURA uses PostgreSQL through EF Core/Npgsql and a single `NeuraDbContext`
for this modular-monolith deployment. Modules own their domain/application
contracts; Infrastructure owns EF configuration and persistence.

## Persisted model

- ASP.NET Core Identity: users, roles, claims, logins, tokens, and role
  membership tables.
- `ProviderAccounts`: owner, provider kind, display name, protected
  credential reference, connection/health state.
- `Agents`: provider-account reference, model ID, role, capabilities,
  context capacity, reliability, enabled/status state.
- `Missions` and `Tasks`: owner/project, mode/status, task capability,
  assignment, ordered dependencies, and trust-labeled task reference
  material.
- `ContextPackages`: mission/task/source agent and structured continuity
  fields (completed/remaining work, decisions, constraints, files, errors,
  tests, dependencies, questions, relevant references).
- `Handoffs`: reason, sender/receiver, package reference, lifecycle state,
  validation notes, and timestamps.
- `ExecutionEvents`: replayable event type/payload and mission/task/agent
  identifiers.
- `CostRecords`: provider/model, normalized input/output tokens, estimated
  cost, and mission/task/agent links.
- `Memories`, `KnowledgeNodes`, `KnowledgeEdges`, `Notifications`, and
  `AuditLogs` for project state, graph knowledge, user-facing updates, and
  sensitive action history.

Provider API keys are encrypted before they reach `ProviderAccounts`; the
column stores only a protected value. Credentials are never cached in a
view model. At runtime, `Neura:Database:PasswordFile` can supply the PostgreSQL
password from an absolute, mounted secret file; it overrides any password in
the connection string. Keep passwords out of committed configuration and
logs. The migration design-time factory still requires an explicit connection
string and should be given deployment secrets only through a secure local
process environment.

## EF mapping notes

`NeuraDbContext.OnModelCreating` explicitly configures collection value
converters and deep value comparers for agent capabilities, task dependency
IDs, trust-labeled references, memory tags, and context package lists. This
is required so in-place collection changes are detected by EF Core. A
PostgreSQL Testcontainers integration test covers relational persistence
for the model.

The hosted worker uses `ExecuteUpdateAsync` with a status predicate to
claim queued missions atomically. That update is the current cross-instance
duplicate-start safeguard.

## Schema lifecycle and release gate

`DatabaseSchemaInitializer` permits `Database.EnsureCreatedAsync()` only
under Development or Testing, where databases are disposable. Staging and
Production require a relational database, at least one checked-in EF Core
migration, and no pending migrations before the application starts. The web
process does not run schema-changing DDL in retained environments; the
release pipeline must apply reviewed migrations before deploying the web
release.

There is **currently no checked-in EF Core migration or model snapshot**, so
Staging/Production intentionally fail fast with an actionable startup error.
Do not bypass this check with `EnsureCreated` against retained data. A
`NeuraDbContextFactory` now gives EF tooling a stable design-time entry point
and requires an explicit connection string instead of falling back to a
committed password. After installing the matching `dotnet-ef` tool, generate
and review the baseline from the repository root:

```bash
NEURA_EF_CONNECTION_STRING='Host=...;Database=neura;Username=...;Password=...' \
  dotnet ef migrations add InitialCreate \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/Web/Web.csproj \
  --context NeuraDbContext \
  --output-dir Persistence/Migrations
```

Review the generated migration and snapshot, then validate fresh install and
upgrade behavior against PostgreSQL. Apply migrations in the dedicated
release/deployment step before starting the web tier, using the same project
and connection configuration with `dotnet ef database update`. Use the
repository's .NET SDK/EF tooling; a hand-authored baseline is not an
acceptable substitute.

## Indexes and growth

EF creates primary-key indexes. Mission polling, owner-scoped pages, and
large execution timelines will need reviewed composite indexes (for
example mission status/created time, mission owner/created time, event
mission/timestamp, cost mission/timestamp, provider owner/state). Add and
measure them in the initial migration before scaling event history.
Execution events, notifications, costs, and audit history also need an
explicit retention/partitioning policy as volume grows.
