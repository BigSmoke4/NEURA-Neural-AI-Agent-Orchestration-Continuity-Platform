# Database

PostgreSQL via EF Core (`Npgsql.EntityFrameworkCore.PostgreSQL`).

Implemented tables (via `NeuraDbContext`): AspNetUsers/Roles (Identity),
ProviderAccounts, Agents, Missions, Tasks, Handoffs, Memories,
KnowledgeNodes, KnowledgeEdges.

Not yet modeled as EF entities (domain types exist, persistence doesn't):
ContextPackages/ContextItems (currently transient, built per-handoff),
ProviderUsage/TokenUsage/CostRecords, AgentHealth/ProviderHealth,
AuditLogs, Notifications, SystemSettings. Adding these is straightforward
— follow the same `Entity`-derived pattern used by `Agent`/`Mission` and
register a `DbSet<T>` + `OnModelCreating` mapping.

Run migrations:

```bash
cd src/Web
dotnet ef migrations add InitialCreate --project ../Infrastructure --startup-project .
dotnet ef database update --project ../Infrastructure --startup-project .
```
