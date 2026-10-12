using Microsoft.EntityFrameworkCore;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.Orchestration.Application;

namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Persists each handoff state transition. The first call inserts the
/// record; later calls update the same tracked record, preserving Pending →
/// InProgress → Validating → Completed/Rejected for audit and replay.
/// </summary>
public sealed class EfHandoffSink : IHandoffSink
{
    private readonly NeuraDbContext _db;

    public EfHandoffSink(NeuraDbContext db) => _db = db;

    public async Task SaveAsync(HandoffRecord handoff, CancellationToken cancellationToken = default)
    {
        if (_db.Entry(handoff).State == EntityState.Detached)
        {
            var exists = await _db.Handoffs.AnyAsync(h => h.Id == handoff.Id, cancellationToken);
            if (!exists)
                _db.Handoffs.Add(handoff);
            else
                _db.Handoffs.Update(handoff);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
