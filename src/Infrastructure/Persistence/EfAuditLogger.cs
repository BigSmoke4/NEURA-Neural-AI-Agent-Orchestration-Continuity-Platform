using Neura.Modules.Observability.Domain;

namespace Neura.Infrastructure.Persistence;

/// <summary>EF Core-backed IAuditLogger writing to AuditLogs table.</summary>
public sealed class EfAuditLogger : IAuditLogger
{
    private readonly NeuraDbContext _db;
    public EfAuditLogger(NeuraDbContext db) => _db = db;

    public async Task LogAsync(Guid? userId, string action, string? target, string result, Guid correlationId, string? ipAddress, CancellationToken ct = default)
    {
        var entry = AuditLogEntry.Record(userId, action, target, result, correlationId, ipAddress);
        _db.AuditLogs.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
