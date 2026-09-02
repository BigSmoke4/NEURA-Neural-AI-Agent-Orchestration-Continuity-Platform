using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Orchestration.Application;

namespace Neura.Infrastructure.Persistence;

public sealed class EfContextPackageSink : IContextPackageSink
{
    private readonly NeuraDbContext _db;
    public EfContextPackageSink(NeuraDbContext db) => _db = db;

    public async Task SaveAsync(ContextHandoffPackage package, CancellationToken ct = default)
    {
        _db.ContextPackages.Add(package);
        await _db.SaveChangesAsync(ct);
    }
}
