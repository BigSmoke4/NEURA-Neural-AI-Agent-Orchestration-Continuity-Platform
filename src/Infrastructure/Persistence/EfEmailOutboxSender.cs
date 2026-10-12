using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Email;
using Neura.Modules.Orchestration.Application;

namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Persists encrypted email payloads before returning to application callers.
/// The transport worker can retry after process restarts; password-reset and
/// confirmation links are never stored as plaintext or written to logs.
/// </summary>
public sealed class EfEmailOutboxSender : IEmailSender
{
    private const int MaxRecipientLength = 320;
    private const int MaxSubjectLength = 500;
    private const int MaxBodyLength = 100_000;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly NeuraDbContext _db;
    private readonly IDataProtector _protector;

    public EfEmailOutboxSender(NeuraDbContext db, IDataProtectionProvider protectionProvider)
    {
        _db = db;
        _protector = protectionProvider.CreateProtector("Neura.EmailOutbox.Payload.v1");
    }

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken ct = default)
    {
        Validate(toAddress, subject, body);

        var payload = JsonSerializer.Serialize(new EmailOutboxPayload(toAddress, subject, body), SerializerOptions);
        var protectedPayload = _protector.Protect(payload);
        _db.EmailOutboxMessages.Add(EmailOutboxMessage.Queue(protectedPayload, DateTime.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    private static void Validate(string toAddress, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(toAddress) || toAddress.Length > MaxRecipientLength)
            throw new ArgumentException("The email recipient is missing or too long.", nameof(toAddress));
        try
        {
            _ = new MailAddress(toAddress);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The email recipient is not a valid address.", nameof(toAddress), exception);
        }

        if (string.IsNullOrWhiteSpace(subject) || subject.Length > MaxSubjectLength || subject.Contains('\r') || subject.Contains('\n'))
            throw new ArgumentException("The email subject is missing, too long, or contains line breaks.", nameof(subject));
        if (body is null || body.Length > MaxBodyLength)
            throw new ArgumentException("The email body is missing or too long.", nameof(body));
    }

}
