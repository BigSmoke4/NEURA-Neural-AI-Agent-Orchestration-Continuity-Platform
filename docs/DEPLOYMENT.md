# Deployment

## Docker (recommended)

```bash
cp .env.example .env   # set POSTGRES_PASSWORD
docker compose up --build
```

This builds `Web` from the included multi-stage `Dockerfile`, runs
Postgres 16 with a health check gate, and mounts a persistent volume for
the Data Protection key ring (`/app/dp-keys`) so encrypted provider
credentials remain decryptable across container restarts.

## Manual deployment

1. Provision PostgreSQL 14+.
2. Set `ConnectionStrings:Default` and any provider API keys via
   environment variables or a secret manager — never in
   `appsettings.json`.
3. Run EF Core migrations (`dotnet ef database update`) as part of your
   deploy pipeline, not at app startup, for review and rollback safety.
4. `dotnet publish -c Release -o out`, then run
   `dotnet out/Neura.Web.dll` behind a reverse proxy (nginx/IIS/Azure
   App Service) with HTTPS termination.
5. Point health checks (`/health`, `/health/live`, `/health/ready`) at
   your platform's liveness/readiness probes.
6. If you scale to multiple instances:
   - Add a SignalR backplane (Redis) so neural events reach every
     connected browser regardless of which instance handled the
     originating request.
   - Point Data Protection at a shared key ring (see docs/SECURITY.md)
     instead of the default file-system store, or encrypted provider
     credentials saved by one instance won't decrypt on another.
   - Back the rate limiter with a distributed store instead of the
     in-process fixed-window limiter.
7. Configure an OpenTelemetry trace exporter for your environment
   (Jaeger/Prometheus/OTLP/Application Insights) — tracing
   instrumentation is registered in `Program.cs` but no exporter ships
   by default.
8. Optional: configure `Neura:Smtp` for email delivery of significant
   notifications, `Neura:OAuth:{ProviderKind}` for an OAuth connection
   flow (Google's real endpoints are the shipped example — see
   `appsettings.example.json`), and ensure the host running NEURA has a
   reachable Docker daemon if you want the Admin-only `/Sandbox` screen
   to work.

## CI

`.github/workflows/ci.yml` restores, builds, and runs the `Neura.Tests`
suite on every push/PR to `main`.
