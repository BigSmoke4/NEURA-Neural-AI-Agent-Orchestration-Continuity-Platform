using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Neura.Web.Startup;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Production_RejectsWildcardAllowedHosts()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("AllowedHosts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RequiresAbsoluteDataProtectionStoragePath()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "dp-keys"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("absolute path", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_RequiresAbsoluteRsaCertificatePath()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("CertificatePath", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RejectsRelativeSecretFilePaths()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys",
            ["Neura:DataProtection:CertificatePath"] = "/run/secrets/neura-dp.pfx",
            ["Neura:Database:PasswordFile"] = "secrets/postgres-password"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("Neura:Database:PasswordFile", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RejectsPublicOriginNotIncludedInAllowedHosts()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys",
            ["Neura:DataProtection:CertificatePath"] = "/run/secrets/neura-dp.pfx",
            ["Neura:PublicOrigin"] = "https://attacker.example"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("Neura:PublicOrigin", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RequiresTlsSmtpForIdentityEmails()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys",
            ["Neura:DataProtection:CertificatePath"] = "/run/secrets/neura-dp.pfx",
            ["Neura:PublicOrigin"] = "https://neura.example.com",
            ["Neura:Smtp:Host"] = "smtp.example.com",
            ["Neura:Smtp:FromAddress"] = "security@neura.example.com",
            ["Neura:Smtp:EnableSsl"] = "false"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("Require TLS for SMTP", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRequiresSmtpPasswordWhenUsernameIsConfigured()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys",
            ["Neura:DataProtection:CertificatePath"] = "/run/secrets/neura-dp.pfx",
            ["Neura:PublicOrigin"] = "https://neura.example.com",
            ["Neura:Smtp:Host"] = "smtp.example.com",
            ["Neura:Smtp:FromAddress"] = "security@neura.example.com",
            ["Neura:Smtp:EnableSsl"] = "true",
            ["Neura:Smtp:Username"] = "neura@example.com"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("SMTP password secret", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_RejectsUnsafeRedisRateLimitKeyPrefix()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys",
            ["Neura:DataProtection:CertificatePath"] = "/run/secrets/neura-dp.pfx",
            ["Neura:PublicOrigin"] = "https://neura.example.com",
            ["Neura:Redis:ConnectionString"] = "localhost:6379",
            ["Neura:RateLimiting:RedisKeyPrefix"] = "neura:{unsafe}",
            ["Neura:Smtp:Host"] = "smtp.example.com",
            ["Neura:Smtp:FromAddress"] = "security@neura.example.com",
            ["Neura:Smtp:EnableSsl"] = "true"
        });

        var error = Assert.Throws<InvalidOperationException>(
            () => ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production)));

        Assert.Contains("RedisKeyPrefix", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_AcceptsExplicitPersistentPathsAndHostnames()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "neura.example.com;admin.example.com",
            ["Neura:DataProtectionKeyPath"] = "/var/lib/neura/dp-keys",
            ["Neura:DataProtection:CertificatePath"] = "/run/secrets/neura-dp.pfx",
            ["Neura:PublicOrigin"] = "https://neura.example.com",
            ["Neura:Smtp:Host"] = "smtp.example.com",
            ["Neura:Smtp:Port"] = "587",
            ["Neura:Smtp:FromAddress"] = "security@neura.example.com",
            ["Neura:Smtp:EnableSsl"] = "true"
        });

        ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Production));
    }

    [Fact]
    public void Development_AllowsLocalWildcardAndRelativeKeyPath()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["AllowedHosts"] = "*",
            ["Neura:DataProtectionKeyPath"] = "dp-keys"
        });

        ProductionConfigurationValidator.Validate(configuration, Environment(Environments.Development));
    }

    private static IConfiguration BuildConfiguration(IDictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static IHostEnvironment Environment(string environmentName)
        => new TestHostEnvironment { EnvironmentName = environmentName };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Neura.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
