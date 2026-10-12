# Deployment

## Local Docker Compose

```bash
cp .env.example .env
mkdir -p secrets
openssl rand -base64 32 > secrets/postgres_password
chmod 644 secrets/postgres_password # local development only
docker compose up --build
```

The development Compose profile runs PostgreSQL and the MVC host in the
Development environment, waits for PostgreSQL health, binds both ports to
loopback only, and persists PostgreSQL and Data Protection keys in named
volumes. The database password is read from an ignored file-backed secret,
not interpolated into the application connection string. This disposable
profile uses `EnsureCreated`; it is not a production deployment template.
Simulation is selectable per mission; Compose does not configure provider
credentials. Do not publish this HTTP profile directly to the public internet.

## Single-host production Compose example

`docker-compose.production.yml` is a hardened single-host example, not a
substitute for the release gates or platform-specific security review below.
It uses immutable external image references, a file-backed database password,
a certificate secret, protected persistent volumes, a non-root read-only web
container, no published PostgreSQL port, and a loopback-only web listener for
a host reverse proxy. File-backed Compose secrets are bind-mounted from the
host; they are **not** an encrypted secret manager. Restrict host file access,
encrypt host storage, and use an orchestrator/secret manager for multi-host
production deployments.

Prepare the secret files outside Git (`./secrets/` is ignored), fill in every
value in `.env.production.example`, then use the production Compose file. The
web image runs as UID 10001; on rootful Linux Docker, make the database,
SMTP, PFX, and PFX-password files readable by that UID without making them
world-readable (for example, `chown 10001:10001 ./secrets/* && chmod 0400 ./secrets/*`). The
PostgreSQL entrypoint reads its password file as root. Rootless/user-namespaced
Docker may map IDs differently; verify file access under the actual runtime.

```bash
cp .env.production.example .env.production
# Replace image digests, exact host/proxy values, and all secret-file paths.
docker compose --env-file .env.production -f docker-compose.production.yml config
docker compose --env-file .env.production -f docker-compose.production.yml up -d
```

`docker compose config` validates interpolation, not app readiness. The web
process deliberately refuses production startup until the reviewed migration
baseline is present and applied. Back up PostgreSQL and the Data Protection
volume together; test restoring both and decrypting a previously stored
provider credential before relying on the deployment.

## Production checklist

1. Provision PostgreSQL 14+ with a managed private network and encrypted
   storage. Supply the connection details via protected configuration; the
   app accepts `Neura:Database:PasswordFile` so the database password can stay
   out of the connection-string environment variable. Staging/Production
   rejects missing and placeholder connection passwords.
2. **Migration release gate:** no reviewed EF migration baseline is checked
   in yet. Development/Testing may use `EnsureCreated` for disposable data;
   other environments now fail fast unless a relational schema has a complete
   applied migration history. Establish and review the baseline, apply it in a
   dedicated deployment step, and only then start the Staging/Production web
   tier. The application intentionally does not perform production DDL.
3. Mission mode is selected per mission in the UI; there is no global
   `Neura:Mode` switch. Use a Real mission only after a user has connected
   an official provider API key and created an enabled agent with a
   compatible model ID/capability. Keys are encrypted with ASP.NET Core
   Data Protection. Outside Development/Testing, startup requires an absolute
   `Neura:DataProtectionKeyPath` on persistent protected storage and an
   absolute `Neura:DataProtection:CertificatePath` to a currently valid RSA
   PFX with its private key. Supply `Neura:DataProtection:CertificatePassword`
   through a secret manager when needed. This certificate encrypts the key
   ring itself; persist/back up the ring and provision the same certificate
   across instances. Plan certificate rotation so old keys remain decryptable.
4. Terminate HTTPS at a trusted reverse proxy/load balancer. Configure
   `Neura:ForwardedHeaders:KnownProxies` with the proxy's exact source IP
   address(es); the app trusts loopback defaults plus only those explicit
   proxies and processes one forwarded hop. Do not clear the trusted-proxy
   list or accept forwarded headers from arbitrary clients. Enable HSTS,
   set `Neura:Security:CookieSecurePolicy=Always` outside local HTTP-only
   development, and restrict `AllowedHosts` to the deployment's hostnames.
   Non-Development/Testing startup rejects an empty or wildcard `AllowedHosts`.
   The production default is `Always`; Compose explicitly overrides it to
   `SameAsRequest` for the local HTTP-only profile.
5. Configure `/health/live` and `/health/ready` for liveness/readiness.
   Readiness currently checks PostgreSQL; liveness is process-level.
6. Configure OTLP export (`Neura:Observability:OtlpEndpoint`) and a
   protected structured-log destination. OpenTelemetry exports framework
   hosting/Kestrel/HTTP-client metrics plus bounded-cardinality provider
   execution outcome/latency and successful token/context measurements,
   neural-event persistence, and SignalR publish counters/durations. Task duration,
   handoff/database latency, and connection-count metrics remain incomplete;
   configure dashboards and alerts for the available measurements.
7. For multi-instance deployments, configure a shared Redis endpoint with
   `Neura:Redis:ConnectionString`, shared Data Protection key storage, and the
   SignalR backplane. The same Redis setting moves session storage and all
   global/named rate-limit windows to atomic cluster-shared counters; the
   optional key namespace is `Neura:RateLimiting:RedisKeyPrefix`. Without Redis,
   rate limits remain process-local. A configured Redis connection is required
   at startup; rate-limited requests fail closed with 503 during Redis errors,
   while `/health/live` remains process-level. Test Redis failover, ACL/TLS,
   key isolation, and quotas across app instances before relying on them.
   Mission claims are atomic, active workers heartbeat persisted state, and a
   stale-run sweep fails uncertain work without retrying provider requests.
   Tune `Neura:MissionRecovery` for the deployment and use synchronized UTC
   clocks across nodes. This is not checkpointed recovery or exactly-once
   execution; inspect failed mission timelines before manually creating
   replacement work.
8. Set privileged registration role codes only via a secret manager if
   controlled Operator/Auditor/Admin self-registration is needed. There is no
   default code; ordinary signup grants only `User`.
9. Configure TLS-enabled SMTP host/port/sender and a password secret when
   authentication is used. Staging/Production require SMTP for email
   confirmation and password reset, and require `Neura:PublicOrigin` to build
   trusted links. Delivery is not backed by a durable outbox: configure SMTP
   monitoring and test failure/retry behavior before release. Confirmation
   and reset links carry short-lived Identity tokens in query strings; redact
   those parameters in reverse-proxy access logs. Development and Testing do
   not require confirmed-email sign-in.
10. Keep `/Sandbox` disabled/restricted to an isolated operator network.
    It requires a Docker daemon and is manual only; it is not an automatic
    agent tool.

## CI and release gate

`.github/workflows/ci.yml` restores/builds/tests the solution, runs unit and
integration tests, and has a Playwright browser job. The PostgreSQL
Testcontainers test requires Docker and fails if its container cannot start,
so CI must provide a working Docker daemon. In this coding environment the
.NET SDK/Docker daemon were unavailable, so run the CI workflow and
smoke-test the target environment before release.
