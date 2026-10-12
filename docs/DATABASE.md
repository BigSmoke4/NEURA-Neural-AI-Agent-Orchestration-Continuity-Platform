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
- `EmailOutboxMessages` for durable SMTP work. Recipient, subject, and body
  are serialized together and protected with ASP.NET Core Data Protection;
  ciphertext is cleared after successful delivery while minimal delivery
  metadata remains.

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

## Durable email outbox

`IEmailSender` durably enqueues a Data Protection-encrypted payload before
returning to Identity or notification callers. `EmailOutboxWorker` is a
separate hosted drain loop; it acquires relational compare-and-set leases so
multiple web instances share one queue, records only the exception type,
retries with bounded exponential backoff, and dead-letters after the configured
attempt limit. Successful SMTP acceptance is followed by a sent marker and
ciphertext removal. A crash between SMTP acceptance and the marker can result
in an at-least-once duplicate; the outbox does not promise exactly-once mail.

The worker depends on all instances sharing a persistent, decryptable Data
Protection key ring. Staging/Production already require that protected key-ring
configuration. Dead-letter review/replay tooling and sent-metadata retention
cleanup remain operational follow-ups; do not place plaintext tokens or raw
SMTP responses in diagnostics.

## Schema lifecycle and release gate

`DatabaseSchemaInitializer` permits `Database.EnsureCreatedAsync()` only
under Development or Testing, where databases are disposable. Staging and
Production require a relational database, the checked-in EF migration set,
and no pending migrations before the application starts. The web process
does not run schema-changing DDL in retained environments; the release
pipeline must apply reviewed migrations before deploying the web release.

The repository now contains the generated `InitialBaseline` migration and
`NeuraDbContextModelSnapshot` under
`src/Infrastructure/Persistence/Migrations`. CI checks that the snapshot
matches the current model, and the PostgreSQL Testcontainers gate applies the
migration to an empty database before exercising relational persistence and
outbox leases. Do not bypass the production gate with `EnsureCreated`.

`NeuraDbContextFactory` gives EF tooling a stable design-time entry point and
requires `NEURA_EF_CONNECTION_STRING` (or `ConnectionStrings__Default`) rather
than falling back to a committed password. Use the matching EF Core 8.0.30
tool and a secure process environment. Generate and review future model
changes from the repository root:

```bash
NEURA_EF_CONNECTION_STRING="$NEURA_EF_CONNECTION_STRING" \
  dotnet ef migrations add AddReviewedChange \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/Web/Web.csproj \
  --context NeuraDbContext \
  --output-dir Persistence/Migrations
```

Review both the migration and model snapshot. Validate a fresh install and
any supported upgrade against PostgreSQL, then apply with the same explicit
project/context configuration in the dedicated deployment step:

```bash
NEURA_EF_CONNECTION_STRING="$NEURA_EF_CONNECTION_STRING" \
  dotnet ef database update \
  --project src/Infrastructure/Infrastructure.csproj \
  --startup-project src/Web/Web.csproj \
  --context NeuraDbContext
```

The baseline creates the complete schema and is appropriate for an empty
PostgreSQL database. If a retained database was previously created with
`EnsureCreated` or manual DDL, do not apply it blindly; plan and rehearse a
separate schema-adoption procedure before production use.

## Indexes and growth

EF creates primary-key indexes. Mission polling, owner-scoped pages, and
large execution timelines will need reviewed composite indexes (for
example mission status/created time, mission owner/created time, event
mission/timestamp, cost mission/timestamp, provider owner/state). Add and
measure them in the initial migration before scaling event history.
Execution events, notifications, costs, and audit history also need an
explicit retention/partitioning policy as volume grows.
