using Neura.Shared;

namespace Neura.Modules.AgentManagement.Domain;

public enum AgentCapability
{
    Coding, Reasoning, Research, LargeContext, Testing,
    Security, Architecture, Database, Documentation, DevOps, Planning
}

public enum AgentStatus
{
    Online, Busy, Thinking, Executing, Warning,
    ContextCritical, Offline, Failed, Recovering
}

public class Agent : Entity
{
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public Guid ProviderAccountId { get; private set; }
    public string ModelId { get; private set; } = default!;
    public string Role { get; private set; } = default!;
    public int Priority { get; private set; }
    public decimal CostProfile { get; private set; }
    public int LatencyProfileMs { get; private set; }
    public int ContextCapacityTokens { get; private set; }
    public double ReliabilityScore { get; private set; } = 1.0;
    public bool Enabled { get; private set; } = true;
    public AgentStatus Status { get; private set; } = AgentStatus.Offline;
    public List<AgentCapability> Capabilities { get; private set; } = new();

    private Agent() { }

    public static Agent Create(string name, string description, Guid providerAccountId, string modelId,
        string role, int contextCapacityTokens, IEnumerable<AgentCapability> capabilities)
    {
        return new Agent
        {
            Name = name,
            Description = description,
            ProviderAccountId = providerAccountId,
            ModelId = modelId,
            Role = role,
            ContextCapacityTokens = contextCapacityTokens,
            Capabilities = capabilities.ToList()
        };
    }

    public void SetStatus(AgentStatus status) { Status = status; Touch(); }
    public void RecordReliability(bool success)
    {
        // simple exponential moving average
        ReliabilityScore = (ReliabilityScore * 0.9) + (success ? 0.1 : 0.0);
        Touch();
    }
    public void Disable() { Enabled = false; Touch(); }
    public void Enable() { Enabled = true; Touch(); }
}
