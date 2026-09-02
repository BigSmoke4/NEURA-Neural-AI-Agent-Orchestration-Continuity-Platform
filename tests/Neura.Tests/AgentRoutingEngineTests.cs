using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Orchestration.Application;
using Xunit;

namespace Neura.Tests;

public class AgentRoutingEngineTests
{
    [Fact]
    public void Score_FavorsCapabilityMatch()
    {
        var engine = new AgentRoutingEngine();
        var codingAgent = Agent.Create("Coder", "desc", Guid.NewGuid(), "model", "Coding Agent", 100_000, new[] { AgentCapability.Coding });
        var researchAgent = Agent.Create("Researcher", "desc", Guid.NewGuid(), "model", "Research Agent", 100_000, new[] { AgentCapability.Research });

        var codingScore = engine.Score(codingAgent, nameof(AgentCapability.Coding), 0.9, true, 0.2, 0.2);
        var researchScore = engine.Score(researchAgent, nameof(AgentCapability.Coding), 0.9, true, 0.2, 0.2);

        Assert.True(codingScore.OverallScore > researchScore.OverallScore);
    }

    [Fact]
    public void SelectBest_ReturnsHighestOverallScore()
    {
        var engine = new AgentRoutingEngine();
        var low = new AgentScoreBreakdown(Guid.NewGuid(), "Low", 10, 10, 10, 10, 10, 10, 10);
        var high = new AgentScoreBreakdown(Guid.NewGuid(), "High", 90, 90, 90, 90, 90, 90, 90);

        var best = engine.SelectBest(new[] { low, high });

        Assert.Equal(high.AgentId, best!.AgentId);
    }
}
