namespace Neura.Modules.ContextManagement.Domain;

/// <summary>
/// Structured, minimal-sufficient context transferred between agents
/// during a handoff. Built by the Context Continuity Engine — never a
/// raw dump of the entire prior conversation.
/// </summary>
public sealed class ContextHandoffPackage
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MissionId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid FromAgentId { get; private set; }
    public string Mission { get; private set; } = default!;
    public string CurrentTask { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public List<string> CompletedWork { get; private set; } = new();
    public List<string> RemainingWork { get; private set; } = new();
    public List<string> Decisions { get; private set; } = new();
    public List<string> Constraints { get; private set; } = new();
    public List<string> FilesChanged { get; private set; } = new();
    public List<string> Errors { get; private set; } = new();
    public List<string> Tests { get; private set; } = new();
    public List<string> Dependencies { get; private set; } = new();
    public List<string> OpenQuestions { get; private set; } = new();
    public List<string> RelevantMemory { get; private set; } = new();
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private ContextHandoffPackage() { }

    public static ContextHandoffPackage Build(
        Guid missionId, Guid taskId, Guid fromAgentId,
        string mission, string currentTask, string status,
        List<string> completedWork, List<string> remainingWork,
        List<string> decisions, List<string> constraints,
        List<string> filesChanged, List<string> errors,
        List<string> tests, List<string> dependencies,
        List<string> openQuestions, List<string> relevantMemory)
    {
        return new ContextHandoffPackage
        {
            MissionId = missionId,
            TaskId = taskId,
            FromAgentId = fromAgentId,
            Mission = mission,
            CurrentTask = currentTask,
            Status = status,
            CompletedWork = completedWork,
            RemainingWork = remainingWork,
            Decisions = decisions,
            Constraints = constraints,
            FilesChanged = filesChanged,
            Errors = errors,
            Tests = tests,
            Dependencies = dependencies,
            OpenQuestions = openQuestions,
            RelevantMemory = relevantMemory
        };
    }
}
