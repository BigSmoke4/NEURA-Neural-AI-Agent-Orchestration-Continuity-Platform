using Microsoft.Extensions.Configuration;
using Neura.Web.Startup;
using Npgsql;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class DatabaseConnectionStringResolverTests
{
    [Fact]
    public void MountedPasswordFileOverridesInlineConnectionStringPassword()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neura-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var secretPath = Path.Combine(directory, "postgres-password");

        try
        {
            File.WriteAllText(secretPath, "p@ss;word=with-symbols\n");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=postgres;Database=neura;Username=neura;Password=old-value",
                    ["Neura:Database:PasswordFile"] = secretPath
                })
                .Build();

            var resolved = new NpgsqlConnectionStringBuilder(DatabaseConnectionStringResolver.Resolve(configuration));

            Assert.Equal("postgres", resolved.Host);
            Assert.Equal("p@ss;word=with-symbols", resolved.Password);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("CHANGE_ME")]
    [InlineData("replace_with_secret")]
    public void ProductionRejectsMissingOrPlaceholderConnectionPasswords(string password)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Host=postgres;Database=neura;Username=neura;Password={password}"
            })
            .Build();

        var error = Assert.Throws<InvalidOperationException>(
            () => DatabaseConnectionStringResolver.Resolve(configuration, requirePassword: true));

        Assert.Contains("non-placeholder PostgreSQL password", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyMountedDatabasePasswordIsRejected()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neura-secret-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var secretPath = Path.Combine(directory, "postgres-password");

        try
        {
            File.WriteAllText(secretPath, "\r\n");
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = "Host=postgres;Database=neura;Username=neura",
                    ["Neura:Database:PasswordFile"] = secretPath
                })
                .Build();

            var error = Assert.Throws<InvalidOperationException>(
                () => DatabaseConnectionStringResolver.Resolve(configuration));

            Assert.Contains("password secret is empty", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
