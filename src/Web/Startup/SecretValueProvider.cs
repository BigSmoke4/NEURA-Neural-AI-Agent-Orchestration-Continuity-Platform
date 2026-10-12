using Microsoft.Extensions.Configuration;

namespace Neura.Web.Startup;

/// <summary>Reads deployment secrets from an orchestrator-mounted file when configured.</summary>
public static class SecretValueProvider
{
    /// <summary>
    /// A configured file takes precedence over the inline configuration value.
    /// Only line terminators are removed so other password characters survive.
    /// </summary>
    public static string? Read(IConfiguration configuration, string valueKey, string fileKey)
    {
        var path = configuration[fileKey];
        if (string.IsNullOrWhiteSpace(path))
            return configuration[valueKey];

        if (!Path.IsPathRooted(path))
            throw new InvalidOperationException($"{fileKey} must be an absolute path.");

        return File.ReadAllText(path).TrimEnd('\r', '\n');
    }
}
