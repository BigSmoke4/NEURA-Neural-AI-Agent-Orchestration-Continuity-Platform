using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Neura.Infrastructure.Persistence;
using Neura.Web.Startup;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class DatabaseSchemaInitializerTests
{
    [Fact]
    public async Task Development_CreatesDisposableSchemaFromCurrentModel()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"schema-dev-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        await DatabaseSchemaInitializer.InitializeAsync(db, CreateEnvironment(Environments.Development));

        Assert.Empty(await db.Missions.ToListAsync());
    }

    [Fact]
    public async Task Production_RefusesNonRelationalProviderBeforeSchemaCreation()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"schema-prod-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => DatabaseSchemaInitializer.InitializeAsync(db, CreateEnvironment(Environments.Production)));

        Assert.Contains("require a relational database", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IHostEnvironment CreateEnvironment(string environmentName)
        => new TestHostEnvironment { EnvironmentName = environmentName };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Neura.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
