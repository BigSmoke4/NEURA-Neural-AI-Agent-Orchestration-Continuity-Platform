using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Neura.Infrastructure.Persistence;

namespace Neura.Web.Startup;

/// <summary>
/// Allows model-driven schema creation only for disposable development/test
/// databases. Retained environments must have a reviewed, applied EF migration
/// set before the web process starts; the web role never performs production DDL.
/// </summary>
public static class DatabaseSchemaInitializer
{
    public static async Task InitializeAsync(
        NeuraDbContext db,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            return;
        }

        if (!db.Database.IsRelational())
            throw new InvalidOperationException(
                "Non-development environments require a relational database and an applied EF Core migration baseline.");

        var migrations = db.Database.GetMigrations().ToArray();
        if (migrations.Length == 0)
            throw new InvalidOperationException(
                "Refusing to start outside Development/Testing: this build contains no EF Core migrations. " +
                "Create and review the migration baseline, then apply it with the deployment migration step.");

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
            throw new InvalidOperationException(
                "Refusing to start because database migrations are pending: " +
                string.Join(", ", pendingMigrations) + ". Apply migrations before starting the web tier.");
    }
}
