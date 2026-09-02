using Neura.Modules.ContextManagement.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Persists a ContextHandoffPackage so the Context Explorer screen can
/// show full field-level detail instead of only the HandoffRecord proxy.
/// </summary>
public interface IContextPackageSink
{
    Task SaveAsync(ContextHandoffPackage package, CancellationToken ct = default);
}
