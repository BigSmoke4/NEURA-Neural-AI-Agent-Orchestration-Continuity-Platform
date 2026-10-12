using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Stable EF tooling entry point for migrations. It deliberately requires an
/// explicit connection string and never reads or falls back to a committed
/// development password. EF design-time commands do not connect unless they
/// apply/query the database.
/// </summary>
public sealed class NeuraDbContextFactory : IDesignTimeDbContextFactory<NeuraDbContext>
{
    public NeuraDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("NEURA_EF_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Set NEURA_EF_CONNECTION_STRING (or ConnectionStrings__Default) before running EF Core tooling.");

        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new NeuraDbContext(options);
    }
}
