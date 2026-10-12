using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Neura.Web.RateLimiting;

namespace Neura.Web.Startup;

/// <summary>
/// Rejects deployment defaults that weaken host filtering, leave provider
/// credentials dependent on ephemeral keys, or disable secure Identity email flows.
/// </summary>
public static class ProductionConfigurationValidator
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            return;

        var allowedHosts = (configuration["AllowedHosts"] ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (allowedHosts.Length == 0 || allowedHosts.Any(host => host.Contains('*')))
            throw new InvalidOperationException("Set AllowedHosts to the exact deployment hostnames outside Development/Testing.");

        foreach (var secretFileKey in new[]
        {
            "Neura:Database:PasswordFile",
            "Neura:DataProtection:CertificatePasswordFile",
            "Neura:Smtp:PasswordFile"
        })
        {
            var secretFilePath = configuration[secretFileKey];
            if (!string.IsNullOrWhiteSpace(secretFilePath) && !Path.IsPathRooted(secretFilePath))
                throw new InvalidOperationException($"Set {secretFileKey} to an absolute path outside Development/Testing.");
        }

        var dataProtectionKeyPath = configuration["Neura:DataProtectionKeyPath"];
        if (string.IsNullOrWhiteSpace(dataProtectionKeyPath) || !Path.IsPathRooted(dataProtectionKeyPath))
            throw new InvalidOperationException("Set Neura:DataProtectionKeyPath to an absolute path on persistent protected storage outside Development/Testing.");

        var certificatePath = configuration["Neura:DataProtection:CertificatePath"];
        if (string.IsNullOrWhiteSpace(certificatePath) || !Path.IsPathRooted(certificatePath))
            throw new InvalidOperationException("Set Neura:DataProtection:CertificatePath to an absolute certificate path outside Development/Testing.");

        var publicOriginText = configuration["Neura:PublicOrigin"];
        if (!Uri.TryCreate(publicOriginText, UriKind.Absolute, out var publicOrigin) ||
            !string.Equals(publicOrigin.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !publicOrigin.IsDefaultPort || !string.IsNullOrEmpty(publicOrigin.UserInfo) ||
            !string.IsNullOrEmpty(publicOrigin.Query) || !string.IsNullOrEmpty(publicOrigin.Fragment) ||
            publicOrigin.AbsolutePath != "/" ||
            !allowedHosts.Any(host => string.Equals(host, publicOrigin.Host, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Set Neura:PublicOrigin to an HTTPS origin whose hostname is included in AllowedHosts.");
        }

        if (!string.IsNullOrWhiteSpace(configuration["Neura:Redis:ConnectionString"]))
            _ = RedisRateLimitCounterStore.NormalizeKeyPrefix(configuration["Neura:RateLimiting:RedisKeyPrefix"]);

        var smtpHost = configuration["Neura:Smtp:Host"];
        var smtpFromAddress = configuration["Neura:Smtp:FromAddress"];
        if (string.IsNullOrWhiteSpace(smtpHost) || !IsValidMailAddress(smtpFromAddress))
            throw new InvalidOperationException("Configure Neura:Smtp:Host and a valid Neura:Smtp:FromAddress outside Development/Testing.");

        var smtpPortText = configuration["Neura:Smtp:Port"];
        if (!string.IsNullOrWhiteSpace(smtpPortText) &&
            (!int.TryParse(smtpPortText, out var smtpPort) || smtpPort is < 1 or > 65535))
        {
            throw new InvalidOperationException("Neura:Smtp:Port must be between 1 and 65535.");
        }

        var smtpSslText = configuration["Neura:Smtp:EnableSsl"];
        if (!bool.TryParse(smtpSslText, out var smtpSslEnabled) || !smtpSslEnabled)
            throw new InvalidOperationException("Require TLS for SMTP by setting Neura:Smtp:EnableSsl=true outside Development/Testing.");

        var smtpUsername = configuration["Neura:Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(smtpUsername))
        {
            var smtpPassword = SecretValueProvider.Read(
                configuration,
                "Neura:Smtp:Password",
                "Neura:Smtp:PasswordFile");
            if (string.IsNullOrEmpty(smtpPassword))
                throw new InvalidOperationException("Configure an SMTP password secret when Neura:Smtp:Username is set.");
        }
    }

    private static bool IsValidMailAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;

        try
        {
            return new MailAddress(address).Address.Contains('@');
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
