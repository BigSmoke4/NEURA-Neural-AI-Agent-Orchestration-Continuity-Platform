using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neura.Modules.Orchestration.Application;

namespace Neura.Infrastructure.Email;

public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? PasswordFile { get; set; }
    public string? FromAddress { get; set; }
    public bool EnableSsl { get; set; } = true;
}

/// <summary>
/// SMTP delivery for operational notifications and Identity account flows.
/// Optional notifications remain in-app-only when Host is unset; Staging/Production
/// startup separately requires TLS-enabled SMTP for confirmation and recovery
/// email. Passwords can be supplied through a mounted file.
/// </summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly SmtpOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_options.Host))
        {
            _logger.LogDebug("SMTP not configured (Neura:Smtp:Host unset); skipping email delivery.");
            return;
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = string.IsNullOrEmpty(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, ReadPassword())
        };

        using var message = new MailMessage(_options.FromAddress ?? _options.Username ?? "neura@localhost", toAddress, subject, body);
        await client.SendMailAsync(message, ct);
    }

    private string? ReadPassword()
    {
        if (string.IsNullOrWhiteSpace(_options.PasswordFile))
            return _options.Password;
        if (!Path.IsPathRooted(_options.PasswordFile))
            throw new InvalidOperationException("Neura:Smtp:PasswordFile must be an absolute path.");

        return File.ReadAllText(_options.PasswordFile).TrimEnd('\r', '\n');
    }
}
