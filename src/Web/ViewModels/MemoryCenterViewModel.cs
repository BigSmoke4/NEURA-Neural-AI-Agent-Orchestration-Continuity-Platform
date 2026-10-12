using Neura.Modules.Memory.Domain;

namespace Neura.Web.ViewModels;

public sealed record MemoryProjectChoice(Guid ProjectId, string Label);

public sealed record MemoryCenterViewModel(
    IReadOnlyList<MemoryRecord> Memories,
    IReadOnlyList<MemoryProjectChoice> Projects,
    string? Query);
