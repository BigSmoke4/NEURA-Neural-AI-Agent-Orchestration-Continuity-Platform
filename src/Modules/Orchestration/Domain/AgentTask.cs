using Neura.Modules.ContextManagement.Domain;
using Neura.Shared;

namespace Neura.Modules.Orchestration.Domain;

public enum TaskStatus2
{
    Pending, Assigned, Running, Blocked, Completed, Failed, Cancelled
}

public class AgentTask : Entity
{
    public Guid MissionId { get; private set; }
    public string Title { get; private set; } = default!;
    public string RequiredCapability { get; private set; } = default!;
    public Guid? AssignedAgentId { get; private set; }
    public TaskStatus2 Status { get; private set; } = TaskStatus2.Pending;
    public List<Guid> DependsOn { get; private set; } = new();
    public int Order { get; private set; }

    /// <summary>
    /// Reference material attached to this task, each piece tagged with
    /// where it came from (section 60). Untrusted entries (e.g. scraped
    /// web content) must be surfaced to the agent as non-authoritative
    /// context, never merged indistinguishably into instructions.
    /// </summary>
    public List<TrustLabeledContent> ReferenceMaterial { get; private set; } = new();

    private AgentTask() { }

    public static AgentTask Create(Guid missionId, string title, string requiredCapability, int order, IEnumerable<Guid>? dependsOn = null)
        => new()
        {
            MissionId = missionId,
            Title = title,
            RequiredCapability = requiredCapability,
            Order = order,
            DependsOn = dependsOn?.ToList() ?? new()
        };

    public void AttachReferenceMaterial(string text, ContentTrustLevel trustLevel, string? sourceDescription = null)
    {
        ReferenceMaterial.Add(new TrustLabeledContent(text, trustLevel, sourceDescription));
        Touch();
    }

    public void Assign(Guid agentId) { AssignedAgentId = agentId; Status = TaskStatus2.Assigned; Touch(); }
    public void Start() { Status = TaskStatus2.Running; Touch(); }
    public void Complete() { Status = TaskStatus2.Completed; Touch(); }
    public void Fail() { Status = TaskStatus2.Failed; Touch(); }
}
