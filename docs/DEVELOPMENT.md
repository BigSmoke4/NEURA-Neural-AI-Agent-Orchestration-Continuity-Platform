# Development

```bash
dotnet restore
cd src/Web
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=neura;Username=neura;Password=devpass"
dotnet ef database update --project ../Infrastructure --startup-project .
dotnet run
```

No provider account or database write is required to see the core demo:
`POST /Brain/RunSimulationDemo` runs entirely against the in-memory
`SimulationAIProvider` and only touches your database if you wire mission
persistence in — the current controller demonstrates the orchestration
flow without requiring EF at all, so you can explore the graph before
setting up Postgres if you'd like.
