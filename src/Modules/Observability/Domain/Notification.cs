namespace Neura.Modules.Observability.Domain;

public enum NotificationKind
{
    ContextWarning, HandoffCompleted, AgentFailure, MissionCompleted,
    ProviderDisconnected, SecurityEvent, CostThreshold
}

/// <summary>Persisted in-app notification (section 79).</summary>
public class Notification
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public NotificationKind Kind { get; private set; }
    public Guid? MissionId { get; private set; }
    public string Message { get; private set; } = default!;
    public bool IsRead { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private Notification() { }

    public static Notification Create(NotificationKind kind, Guid? missionId, string message)
        => new() { Kind = kind, MissionId = missionId, Message = message };

    public void MarkRead() => IsRead = true;
}
