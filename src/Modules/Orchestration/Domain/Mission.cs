using Neura.Shared;

namespace Neura.Modules.Orchestration.Domain;

public enum MissionMode { Real, Simulation }

public class Mission : Entity
{
    public Guid ProjectId { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Objective { get; private set; } = default!;
    public MissionMode Mode { get; private set; }
    public MissionStatus Status { get; private set; } = MissionStatus.Created;
    public List<AgentTask> Tasks { get; private set; } = new();

    private Mission() { }

    public static Mission Create(Guid projectId, string title, string objective, MissionMode mode, Guid? ownerUserId = null)
        => new()
        {
            ProjectId = projectId,
            OwnerUserId = ownerUserId,
            Title = title,
            Objective = objective,
            Mode = mode
        };

    public void AddTask(AgentTask task) { Tasks.Add(task); Touch(); }
    public void SetStatus(MissionStatus status) { Status = status; Touch(); }
}

public enum MissionStatus
{
    Created, Planning, Assigned, Running, Waiting,
    ContextWarning, ContextExhausted, HandoffPending, HandoffInProgress,
    Transferred, Validating, Completed, Failed, Retrying, Cancelled
}
