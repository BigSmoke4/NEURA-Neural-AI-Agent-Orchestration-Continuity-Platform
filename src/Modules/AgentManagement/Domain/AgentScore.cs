namespace Neura.Modules.AgentManagement.Domain;

/// <summary>
/// Explainable routing score. Weights are configurable via
/// AgentRoutingOptions (see Orchestration module).
/// </summary>
public sealed record AgentScoreBreakdown(
    Guid AgentId,
    string AgentName,
    double CapabilityScore,
    double ContextScore,
    double ReliabilityScore,
    double HealthScore,
    double CostScore,
    double LatencyScore,
    double OverallScore);
