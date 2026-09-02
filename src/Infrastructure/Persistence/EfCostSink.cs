using Neura.Modules.Orchestration.Application;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Infrastructure.Persistence;

public sealed class EfCostSink : ICostSink
{
    private readonly NeuraDbContext _db;
    public EfCostSink(NeuraDbContext db) => _db = db;

    public async Task RecordAsync(ProviderKind provider, string modelId, int inputTokens, int outputTokens,
        decimal estimatedCost, Guid missionId, Guid taskId, Guid agentId, CancellationToken ct = default)
    {
        var record = CostRecord.Record(provider, modelId, inputTokens, outputTokens, estimatedCost, missionId, taskId, agentId);
        _db.CostRecords.Add(record);
        await _db.SaveChangesAsync(ct);
    }
}
