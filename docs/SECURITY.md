# Security

- **No credential scraping.** `IAIProvider` adapters call each vendor's
  official REST API.
- **Credentials encrypted at rest.** `ProvidersController.Connect`
  encrypts the submitted API key via `ICredentialProtector`
  (`DataProtectionCredentialProtector`, backed by ASP.NET Core Data
  Protection) before it is ever written to
  `AIProviderAccount.ProtectedCredentialRef`. Decryption happens only at
  the point of use inside `ProviderFactory`, immediately before building
  a real-mode provider adapter; the plaintext key is never logged,
  never cached beyond that call, and never rendered back to any view
  (the Security Center screen shows only a masked `••••1234` suffix).
  In a multi-instance deployment, replace the default file-system key
  ring (`PersistKeysToFileSystem`) with a shared store your instances
  all have access to (e.g. a database-backed key ring or a cloud key
  vault) — see the comment in `Program.cs`.
- **Identity** — ASP.NET Core Identity with a hardened password policy
  (10+ characters, uppercase + non-alphanumeric required), lockout after
  5 failed attempts for 15 minutes, secure/HttpOnly/SameSite cookies, and
  unique-email enforcement. Wire up email confirmation and password-reset
  token delivery before enabling `RequireConfirmedAccount`.
- **Authorization** — policy-based (`ManageAgents`, `ManageProviders`,
  `ExecuteMission`, `ViewMemory`, `ViewCredentials`, `ViewAuditLogs`,
  `ManageSystem`), enforced server-side in `Program.cs`; apply
  `[Authorize(Policy = "...")]` on the remaining controllers as your
  role model solidifies (currently declared but not yet attached to
  every action — see Future Work in the README).
- **CSRF** — `AutoValidateAntiforgeryTokenAttribute` is registered
  globally for all MVC actions, and every mutating form in the Razor
  views renders `@Html.AntiForgeryToken()`.
- **Rate limiting** — `AddRateLimiter` in `Program.cs` applies a global
  per-IP limiter plus named, tighter limiters (`login`,
  `mission-creation`, `provider-config`) on the endpoints most worth
  protecting from abuse (section 42). The in-process fixed-window
  limiter is fine for a single instance; back it with a distributed
  store before scaling horizontally.
- **Security headers** — `SecurityHeadersMiddleware` sets
  `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy`, and a
  restrictive `Permissions-Policy` on every response. A
  Content-Security-Policy is scaffolded but commented out — turning it
  on requires allow-listing the exact CDN hosts the neural graph's
  Cytoscape.js/SignalR scripts load from.
- **Global exception handling** — `GlobalExceptionMiddleware` logs the
  full exception server-side with a correlation ID and returns only a
  generic message (plus that ID) to the client; stack traces,
  connection strings, and credentials are never rendered to users.
- **Prompt injection** — the domain model deliberately keeps
  `ContextHandoffPackage` fields structured and typed rather than a
  single blob, so untrusted content pulled into a task doesn't silently
  become "instructions" for the next agent. A full trust-labeling layer
  (System Instructions vs Untrusted External Content vs Agent Output) is
  a recommended addition once real web/document ingestion is built —
  see Future Work.
- **Sandbox** — no arbitrary command execution exists anywhere in this
  codebase. If/when a coding agent needs to run generated code, implement
  `IExecutionSandbox` behind a container boundary before allowing it —
  this remains unimplemented; see Future Work.
