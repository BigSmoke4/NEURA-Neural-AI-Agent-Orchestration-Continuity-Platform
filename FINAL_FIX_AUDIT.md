# NEURA Final Fix Audit

This build is based on the previously Docker-verified `NEURA-fixed-dp-keys` source.

## Fixed in this final pass

- Preserved the explicit `MemoryRecord.MemoryId` EF primary-key mapping.
- Preserved the Docker Data Protection key-ring permission fix and non-root runtime user.
- Updated OpenTelemetry packages from 1.9.0 to 1.18.0, including the OTLP exporter that was producing the NU1902 vulnerability warning.
- Updated .NET 8 Microsoft packages to the current 8.0.30 patch line where referenced, and Npgsql EF Core to 8.0.11.
- Removed the Docker `HTTP_PORTS`/`ASPNETCORE_URLS` mismatch warning by using `ASPNETCORE_HTTP_PORTS=8080`.
- Made authentication/session cookies `SameAsRequest`: the bundled HTTP Docker deployment can authenticate, while HTTPS deployments still receive Secure cookies.
- Prevented login `returnUrl` open redirects by accepting only local URLs.
- Changed Google Gemini API-key transport from a URL query parameter to the `x-goog-api-key` header.
- Made provider capability pricing honor configured `Neura:ModelPricing` overrides for Anthropic, OpenAI, and Google.
- Made OAuth state single-use and validate provider kinds before token exchange; callback now resolves the signed-in Identity user ID correctly.
- Added real-mode mission execution: a real mission now loads connected provider accounts belonging to its owner, decrypts credentials through the existing provider factory, and uses a second connected provider as a real handoff fallback when available. Simulation mode remains deterministic and credential-free.
- Added mission owner identity to the domain model and set it from the authenticated user on mission creation.
- Persisted mission status transitions to Running/Completed/Failed during orchestration.
- Hardened the manual Docker sandbox with capability dropping, `no-new-privileges`, and a constrained temporary filesystem while retaining network isolation/resource limits/read-only execution.
- Kept credentials out of rendered views and out of Google provider URLs.
- Added this audit file for traceability.

## Verification performed here

- XML parsing of all `.csproj` files passed.
- JSON parsing of `appsettings.json` and `appsettings.example.json` passed.
- Source inspection found no TODO/FIXME/NotImplementedException placeholders.
- The environment used for this packaging pass does not contain the .NET SDK or Docker daemon, so a fresh `dotnet build`, test run, and Docker rebuild must still be executed on the user's Windows/Docker Desktop machine.

## First run

Because the model now adds `Mission.OwnerUserId`, recreate the development database/schema if the existing database was created by an earlier build:

```powershell
docker compose down -v
docker compose build --no-cache
docker compose up
```

Do not commit real provider keys, SMTP passwords, OAuth client secrets, or PostgreSQL passwords to the repository.
