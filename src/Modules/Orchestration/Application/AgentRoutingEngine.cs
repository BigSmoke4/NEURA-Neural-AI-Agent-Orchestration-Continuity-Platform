using Neura.Modules.AgentManagement.Domain;

namespace Neura.Modules.Orchestration.Application;

public sealed class AgentRoutingWeights
{
    public double Capability { get; set; } = 0.30;
    public double Context { get; set; } = 0.20;
    public double Reliability { get; set; } = 0.20;
    public double Health { get; set; } = 0.15;
    public double Cost { get; set; } = 0.10;
    public double Latency { get; set; } = 0.05;
}

/// <summary>
/// Deterministic scoring/routing engine. AI-assisted routing can be layered
/// on top later, but this fallback must always be available and explainable.
/// </summary>
public sealed class AgentRoutingEngine
{
    private readonly AgentRoutingWeights _weights;

    public AgentRoutingEngine(AgentRoutingWeights? weights = null) => _weights = weights ?? new AgentRoutingWeights();

    public AgentScoreBreakdown Score(Agent agent, string requiredCapability, double contextAvailableRatio,
        bool isHealthy, double normalizedCost, double normalizedLatency)
    {
        double capabilityScore = agent.Capabilities.Any(c => c.ToString() == requiredCapability) ? 100 : 20;
        double contextScore = contextAvailableRatio * 100;
        double reliabilityScore = agent.ReliabilityScore * 100;
        double healthScore = isHealthy ? 100 : 0;
        double costScore = (1 - normalizedCost) * 100;
        double latencyScore = (1 - normalizedLatency) * 100;

        double overall =
            capabilityScore * _weights.Capability +
            contextScore * _weights.Context +
            reliabilityScore * _weights.Reliability +
            healthScore * _weights.Health +
            costScore * _weights.Cost +
            latencyScore * _weights.Latency;

        return new AgentScoreBreakdown(agent.Id, agent.Name, capabilityScore, contextScore,
            reliabilityScore, healthScore, costScore, latencyScore, Math.Round(overall, 1));
    }

    public AgentScoreBreakdown? SelectBest(IEnumerable<AgentScoreBreakdown> candidates)
        => candidates.OrderByDescending(c => c.OverallScore).FirstOrDefault();
}
