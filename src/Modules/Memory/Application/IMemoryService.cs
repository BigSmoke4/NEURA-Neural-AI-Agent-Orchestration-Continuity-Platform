using Neura.Modules.Memory.Domain;

namespace Neura.Modules.Memory.Application;

/// <summary>
/// Project-scoped memory access for orchestration. Implementations must
/// enforce expiration and retrieval limits; callers must present returned
/// text as untrusted context rather than executable instructions.
/// </summary>
public interface IMemoryService
{
    Task<IReadOnlyList<MemoryRecord>> RetrieveRelevantAsync(
        Guid projectId,
        string query,
        int maxRecords,
        CancellationToken cancellationToken = default);

    Task<int> PurgeExpiredAsync(
        DateTime nowUtc,
        int maxRecords,
        CancellationToken cancellationToken = default);
}
