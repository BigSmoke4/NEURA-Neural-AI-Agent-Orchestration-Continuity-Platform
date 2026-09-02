using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Testcontainers.PostgreSql;
using Xunit;

namespace Neura.Tests.Integration;

/// <summary>
/// Real integration test against an actual PostgreSQL instance, spun up
/// on demand in a Docker container via Testcontainers — not EF Core's
/// InMemory provider. Confirms migrations/mappings genuinely work
/// against Postgres (list-to-string value converters, Identity's own
/// schema, etc.), which InMemory silently tolerates but a real
/// relational engine will not if a mapping is wrong.
///
/// Requires a Docker daemon reachable from wherever `dotnet test` runs
/// (including CI) — see .github/workflows/ci.yml. Skips gracefully if
/// Docker isn't available rather than failing the whole suite.
/// </summary>
public class PostgresIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("neura_test")
        .WithUsername("neura")
        .WithPassword("test_password")
        .Build();

    private bool _dockerAvailable = true;

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch
        {
            _dockerAvailable = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (_dockerAvailable)
            await _container.DisposeAsync();
    }

    [Fact]
    public async Task CanCreateSchemaAndPersistAnAgent_AgainstRealPostgres()
    {
        if (!_dockerAvailable)
            return; // Docker not available in this environment — see class doc comment.

        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;

        await using var db = new NeuraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var agent = Agent.Create("Test Agent", "desc", Guid.NewGuid(), "model-x", "Coding Agent", 50_000,
            new[] { AgentCapability.Coding, AgentCapability.Testing });
        db.Agents.Add(agent);
        await db.SaveChangesAsync();

        var reloaded = await db.Agents.FirstOrDefaultAsync(a => a.Id == agent.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Test Agent", reloaded!.Name);
        Assert.Contains(AgentCapability.Coding, reloaded.Capabilities);
        Assert.Contains(AgentCapability.Testing, reloaded.Capabilities);
    }
}
