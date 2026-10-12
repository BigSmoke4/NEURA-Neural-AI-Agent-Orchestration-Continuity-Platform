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

    /// <summary>
    /// Place a newly-created mission in the durable database work queue.
    /// Missions cannot be enqueued more than once or restarted implicitly;
    /// retries require an explicit state transition rather than a duplicate POST.
    /// </summary>
    public void Queue()
    {
        if (Status != MissionStatus.Created)
            throw new InvalidOperationException($"Only a newly-created mission can be queued (current status: {Status}).");

        Status = MissionStatus.Queued;
        Touch();
    }

    public void SetStatus(MissionStatus status) { Status = status; Touch(); }

    /// <summary>
    /// Refresh the persisted liveness timestamp while a worker owns this run.
    /// This intentionally does not change the mission state.
    /// </summary>
    public void RefreshExecutionHeartbeat()
    {
        if (Status != MissionStatus.Running)
            throw new InvalidOperationException("Only a running mission can refresh its execution heartbeat.");

        Touch();
    }

    public void Cancel()
    {
        if (Status is MissionStatus.Completed or MissionStatus.Failed or MissionStatus.Cancelled)
            return;

        Status = MissionStatus.Cancelled;
        Touch();
    }
}

public enum MissionStatus
{
    Created, Planning, Assigned, Running, Waiting,
    ContextWarning, ContextExhausted, HandoffPending, HandoffInProgress,
    Transferred, Validating, Completed, Failed, Retrying, Cancelled,
    // Appended to preserve the persisted numeric values of the older states.
    Queued
}
