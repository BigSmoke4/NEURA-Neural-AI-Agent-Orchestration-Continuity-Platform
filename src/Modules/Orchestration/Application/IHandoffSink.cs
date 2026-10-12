using Neura.Modules.Handoff.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Persistence boundary for handoff lifecycle records. The orchestration
/// module owns the transition decisions; the Infrastructure module owns
/// how those transitions are stored.
/// </summary>
public interface IHandoffSink
{
    Task SaveAsync(HandoffRecord handoff, CancellationToken cancellationToken = default);
}
