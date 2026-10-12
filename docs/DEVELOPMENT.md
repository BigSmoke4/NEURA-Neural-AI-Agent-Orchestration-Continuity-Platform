# Development

## Requirements

- .NET 8 SDK
- PostgreSQL 14+ (or Docker Compose)
- Node.js is not required; the UI uses plain modular JavaScript and Razor.

## Start with Docker Compose

```bash
cp .env.example .env
mkdir -p secrets
openssl rand -base64 32 > secrets/postgres_password
chmod 644 secrets/postgres_password # local dev only; readable by the non-root container UID
docker compose up --build
```

Keep the generated password file stable while retaining that local database;
changing it requires a coordinated PostgreSQL password rotation. Open
`http://localhost:8081`. The development Compose profile reads the database
password from a file-backed secret, binds both web and PostgreSQL ports to
loopback only, creates a fresh development schema, and persists the Data
Protection key ring in a named volume. Mission mode is selected per mission;
Compose has no provider credentials configured, and Simulation is the first
available mode in the mission form.

## Start locally

```bash
cp src/Web/appsettings.example.json src/Web/appsettings.Development.json
# Set ConnectionStrings:Default using user-secrets or environment variables.
dotnet user-secrets init --project src/Web/Web.csproj
dotnet user-secrets set \
  "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=neura;Username=neura;Password=..." \
  --project src/Web/Web.csproj
dotnet run --project src/Web/Web.csproj
```

A fresh development database is created from the current EF model at app
startup. **This repository does not yet contain an EF migration baseline**;
`EnsureCreatedAsync` will not evolve an existing schema. Use a disposable
local database while developing model changes, and establish/review real
migrations before upgrading persistent production data.

## Provider and agent setup

1. Register and sign in. New accounts receive only the `User` role.
2. Open **Providers** and connect your own OpenAI, Anthropic, or Google AI
   API key. NEURA encrypts it before persistence and never renders it again.
3. Open **Agents** and create an agent referencing that connected account.
   Enter the provider's exact model ID and one or more supported
   capabilities (for example, `Coding`).
4. Create a Real Mode mission and queue it. If no enabled connected agent
   matches the task capability, the mission fails clearly; it never falls
   back to a simulated answer.

OAuth remains disabled until provider-specific token refresh and credential
semantics are implemented. Use the official API-key method for now.

## Deterministic demo

On **Brain**, click **Run Simulation Demo**. The explicitly labeled
Simulation Mode sequence increases the simulator's context usage through
30%, 55%, 72%, 88%, and 96%, persists a structured package and handoff,
then continues on the second simulator. The mission, task, events, context
package, handoff, and result are persisted for later inspection.

Simulation output is not represented as a real model response and does not
create billable cost records.

## Mission worker recovery

The worker defaults to a 20-second liveness heartbeat, a five-minute stale
threshold, a 30-second recovery sweep, and at most 100 stale rows per sweep.
Tune `Neura:MissionRecovery` (for example, via
`Neura__MissionRecovery__StaleRunningTimeoutSeconds`) for the deployment; the
stale timeout is normalized to at least three heartbeat intervals. Recovery
marks uncertain work failed and does not retry it automatically. This avoids
silently repeating provider side effects, but operators must inspect failed
mission events before manually creating replacement work. Multi-instance
hosts should use synchronized UTC clocks.

## Tests

```bash
dotnet restore NEURA.sln
dotnet build NEURA.sln -c Release
dotnet test tests/Neura.Tests/Neura.Tests.csproj -c Release
```

`Neura.Tests` contains unit and ASP.NET integration tests. One relational
smoke test uses Testcontainers to create a PostgreSQL schema and round-trip
an Agent; Docker startup failure fails the test fixture, so the test cannot
silently report success without exercising PostgreSQL. `Neura.E2E.Tests`
drives Chromium with Playwright against a separately
started app; see the CI workflow for browser installation and server launch.
