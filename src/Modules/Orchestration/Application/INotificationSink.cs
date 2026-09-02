using Neura.Modules.Observability.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>Section 79: in-app notifications for significant orchestration events.</summary>
public interface INotificationSink
{
    Task NotifyAsync(NotificationKind kind, Guid? missionId, string message, CancellationToken ct = default);
}
