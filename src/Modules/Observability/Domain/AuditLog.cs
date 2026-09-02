namespace Neura.Modules.Observability.Domain;

/// <summary>
/// Every sensitive action recorded per section 40 of the spec:
/// who, what, when, where, target, result, correlation id.
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public string Action { get; private set; } = default!;
    public string? Target { get; private set; }
    public string Result { get; private set; } = default!;
    public Guid CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime TimestampUtc { get; private set; } = DateTime.UtcNow;

    private AuditLogEntry() { }

    public static AuditLogEntry Record(Guid? userId, string action, string? target, string result, Guid correlationId, string? ipAddress)
        => new()
        {
            UserId = userId,
            Action = action,
            Target = target,
            Result = result,
            CorrelationId = correlationId,
            IpAddress = ipAddress
        };
}

public interface IAuditLogger
{
    Task LogAsync(Guid? userId, string action, string? target, string result, Guid correlationId, string? ipAddress, CancellationToken ct = default);
}
