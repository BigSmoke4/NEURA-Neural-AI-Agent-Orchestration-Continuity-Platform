# Security posture

NEURA uses explicit provider APIs; it does not scrape consumer websites,
store provider passwords, or automate browser sessions.

## Implemented controls

- **Provider secrets at rest.** `ProvidersController` encrypts API keys
  through `ICredentialProtector` / ASP.NET Core Data Protection before
  persistence. The plaintext is not rendered and is decrypted only by
  `ProviderFactory` when creating the provider adapter. Data Protection
  keys must be persisted and backed up; all instances in a scaled
  deployment must share the same protected key ring. Rotation replaces the
  old protected value and is audited. Security Center uses a metadata-only
  projection that excludes even the protected credential payload.
- **Deployment secret files.** The web host can read the PostgreSQL password
  from `Neura:Database:PasswordFile` and the Data Protection PFX password from
  `Neura:DataProtection:CertificatePasswordFile`. Mounted files override
  inline values and only a terminal newline is stripped. The provided Compose
  examples use these paths instead of embedding database/certificate passwords
  in container environment variables; file-backed Compose secrets still
  require host permissions, encryption, backups, and rotation controls.
- **Owner-scoped provider management.** Provider owner IDs are derived
  from the authenticated Identity principal, never from form input.
  Provider listing, disconnect, and rotation all query the caller's own
  accounts. Agent creation verifies that the selected connected provider
  belongs to the caller. Mission, task-reference, memory, context, handoff,
  notification, and knowledge reads are scoped by mission/project owner
  for ordinary users. Admin/Operator/Auditor cross-user access is explicit
  in policy and query code.
- **Realtime tenant isolation.** `/hubs/neural` requires authentication.
  The server places connections in a user-specific SignalR group; only
  Admin/Auditor connections join the operations group. Clients cannot join
  arbitrary groups. Mission events are routed using server-owned mission
  ownership, not a client-supplied target.
- **Identity and roles.** ASP.NET Core Identity hashes passwords, enforces
  minimum password policy, lockout, unique email, and secure/HttpOnly
  cookies. Authorization is policy-based and enforced on controllers.
  New users receive only `User`. Privileged self-service role codes have
  no shipped default and must be set via a secret store if enabled. Auditors
  may inspect cross-user operational records but cannot queue missions, run
  the simulation demo, delete memory, or mutate notifications.
- **CSRF and rate limits.** MVC applies antiforgery validation globally;
  mutating forms carry antiforgery tokens. Fixed-window policies are
  partitioned per client IP: login/registration allows 10 requests/minute,
  account recovery allows 10/minute, mission creation allows 20/minute, and
  provider configuration allows 15/minute. Login and registration share one
  bucket; confirmation/resend/reset actions share the recovery bucket. An
  additional 300-request/minute global IP limit applies. Without Redis, windows
  are process-local. Setting `Neura:Redis:ConnectionString` switches the same
  quotas to atomic shared Redis counters keyed by a digest of the normalized IP
  and policy; counter keys expire with their window and storage failures reject
  requests with 503. Redis must remain available for rate-limited requests;
  `/health/live` remains process-level. Trusted forwarded IPs are accepted only
  from configured proxies.
- **SSRF resistance for reference ingestion.** URL ingestion accepts only
  HTTP/HTTPS on standard ports, rejects embedded credentials, disables
  redirects, caps response size/time, and uses a socket connector that
  resolves at connection time and refuses loopback/private/link-local and
  other non-global addresses. Fetched text remains explicitly labeled
  untrusted reference data. Audit/source metadata stores only the origin,
  not query strings or path segments that might contain signed tokens.
- **Prompt-injection labeling.** External material and previous agent
  output are delimited and labeled as untrusted data when assembled into
  the receiving request. The agent output is treated as untrusted output,
  not privileged instructions. This is defense-in-depth, not a guarantee
  that a model will resist every injection.
- **XSS-safe event feed.** SignalR payloads and model/provider text are
  written to DOM `textContent`, never interpolated into `innerHTML`.
  Razor's normal HTML encoding is retained for server-rendered values.
- **Sandbox boundary.** `DockerContainerSandbox` is a manual Admin-only
  tool with network disabled, resource caps, dropped capabilities,
  read-only execution, and `no-new-privileges`. It is not invoked
  automatically from model output; generated commands are never run on
  the application host.
- **Audit trail and exception handling.** Sensitive UI actions are
  recorded in `AuditLogs`. Global exception handling returns generic
  errors with correlation IDs and does not serialize stack traces or
  database/provider secrets to the user.

## Operational caveats

- Cookie policy defaults to `Always` in Production and
  `SameAsRequest` in non-Production environments; Compose overrides it to
  `SameAsRequest` for its local HTTP-only profile. When behind a proxy,
  configure its exact source IP in `Neura:ForwardedHeaders:KnownProxies`;
  untrusted forwarded headers are not accepted.
- Session state uses Redis when `Neura:Redis:ConnectionString` is configured;
  otherwise it is process-local. The same setting enables the shared atomic
  rate limiter described above. Redis is a required deployment dependency when
  enabled; startup fails if its multiplexer cannot connect. Use isolated Redis
  databases or distinct `Neura:RateLimiting:RedisKeyPrefix` values between
  environments.
- `Content-Security-Policy` is enforced with same-origin scripts, no inline
  script blocks/event attributes, a same-host WebSocket endpoint, and no
  object/plugin sources. Cytoscape and SignalR are pinned, self-hosted assets
  with tracked licenses/SHA-256 hashes and browser-enforced SRI digests.
  Inline script blocks and style blocks are blocked. The separate
  `style-src-attr` directive still allows inline style attributes for legacy
  Razor and graph-library style handling; remove that allowance only after
  browser tests confirm every graph and page works with stylesheet-only
  styling.
- Email confirmation and password reset use ASP.NET Core Identity tokens
  delivered by the configured SMTP transport through a durable PostgreSQL
  outbox. The recipient, subject, and body are Data Protection-encrypted at
  rest; error logs store only exception types, successful sends clear the
  ciphertext, and bounded retries terminate in a dead-letter state. This is
  at-least-once delivery, not exactly-once: a crash after SMTP acceptance but
  before the sent marker can produce a duplicate. Share the protected Data
  Protection key ring across instances and monitor dead letters/table growth;
  operator replay and sent-row retention are not implemented. Staging/Production
  require a valid HTTPS `Neura:PublicOrigin` whose host is in `AllowedHosts`, a
  valid sender, and TLS-enabled SMTP; production login requires a confirmed
  email. Confirmation is POST-only after the link opens, and token pages are
  marked no-store with no-referrer. Password-reset response content/status do
  not disclose whether an account exists. Tokens expire after three hours.
- OAuth start/callback reject requests unless a provider is explicitly
  enabled. However, provider-specific bearer-token use and refresh-token
  rotation are not implemented; manually enabling the generic scaffold does
  not make it a supported provider integration. Keep OAuth disabled and use
  official API keys for the supported provider adapters.
- A generated `InitialBaseline` migration and
  `NeuraDbContextModelSnapshot` are checked in. `EnsureCreatedAsync` remains
  restricted to Development/Testing. CI checks model/snapshot parity and
  applies the migration to a fresh PostgreSQL Testcontainer; Staging/Production
  fail fast until every migration is applied. Retained databases created by
  `EnsureCreated` still need a separately reviewed adoption procedure.
- Active missions heartbeat their persisted state. An independent recovery
  worker marks stale runs failed without automatic provider retries, avoiding
  duplicate side effects. This does not provide checkpointed resume or
  exactly-once external execution.
- Staging/Production also reject wildcard host filtering and require an
  absolute persistent Data Protection key path plus an absolute, currently
  valid RSA private-key certificate. The certificate protects the key ring;
  storage permissions, backup, and rotation/decryption continuity still need
  environment-level verification.
