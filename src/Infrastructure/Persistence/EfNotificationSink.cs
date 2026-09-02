using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;

namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Persists every notification, and additionally emails it when SMTP is
/// configured and the kind is significant enough to warrant it
/// (SecurityEvent, AgentFailure, ProviderDisconnected, CostThreshold —
/// not routine context warnings, to avoid inbox noise). The email leg is
/// entirely optional: SmtpEmailSender no-ops silently if unconfigured.
/// </summary>
public sealed class EfNotificationSink : INotificationSink
{
    private static readonly HashSet<NotificationKind> EmailWorthy = new()
    {
        NotificationKind.SecurityEvent, NotificationKind.AgentFailure,
        NotificationKind.ProviderDisconnected, NotificationKind.CostThreshold
    };

    private readonly NeuraDbContext _db;
    private readonly IEmailSender? _emailSender;
    private readonly string? _notifyAddress;

    public EfNotificationSink(NeuraDbContext db, IEmailSender? emailSender = null, Microsoft.Extensions.Configuration.IConfiguration? configuration = null)
    {
        _db = db;
        _emailSender = emailSender;
        _notifyAddress = configuration?["Neura:NotifyEmailAddress"];
    }

    public async Task NotifyAsync(NotificationKind kind, Guid? missionId, string message, CancellationToken ct = default)
    {
        var notification = Notification.Create(kind, missionId, message);
        _db.Notifications.Add(notification);
        await _db.SaveChangesAsync(ct);

        if (_emailSender is not null && !string.IsNullOrEmpty(_notifyAddress) && EmailWorthy.Contains(kind))
        {
            await _emailSender.SendAsync(_notifyAddress, $"NEURA: {kind}", message, ct);
        }
    }
}
