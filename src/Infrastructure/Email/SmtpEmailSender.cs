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
    public string? FromAddress { get; set; }
    public bool EnableSsl { get; set; } = true;
}

/// <summary>
/// Real SMTP delivery for notifications — config-driven via
/// Neura:Smtp; if Host is unset, SendAsync no-ops rather than throwing,
/// so notifications remain in-app-only by default with no configuration
/// required, and email becomes available the moment SMTP settings are
/// supplied without any code change.
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
            _logger.LogDebug("SMTP not configured (Neura:Smtp:Host unset) — skipping email to {ToAddress}.", toAddress);
            return;
        }

        using var client = new SmtpClient(_options.Host, _options.Port)
        {
            EnableSsl = _options.EnableSsl,
            Credentials = string.IsNullOrEmpty(_options.Username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(_options.Username, _options.Password)
        };

        using var message = new MailMessage(_options.FromAddress ?? _options.Username ?? "neura@localhost", toAddress, subject, body);
        await client.SendMailAsync(message, ct);
    }
}
