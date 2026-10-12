using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Neura.Web.Startup;

/// <summary>Builds the PostgreSQL connection string without storing its password in deployment config.</summary>
public static class DatabaseConnectionStringResolver
{
    public static string Resolve(IConfiguration configuration, bool requirePassword = false)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Set ConnectionStrings:Default to the PostgreSQL connection details.");

        var password = SecretValueProvider.Read(
            configuration,
            "Neura:Database:Password",
            "Neura:Database:PasswordFile");
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        if (password is not null)
        {
            if (password.Length == 0)
                throw new InvalidOperationException("The configured PostgreSQL password secret is empty.");
            builder.Password = password;
        }

        if (requirePassword && (string.IsNullOrWhiteSpace(builder.Password) || IsPlaceholder(builder.Password)))
            throw new InvalidOperationException("Configure a non-placeholder PostgreSQL password through a secret file or protected configuration.");

        return builder.ConnectionString;
    }

    private static bool IsPlaceholder(string password)
        => string.Equals(password, "CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
           password.StartsWith("replace_with_", StringComparison.OrdinalIgnoreCase) ||
           password.StartsWith("replace_", StringComparison.OrdinalIgnoreCase);
}
