using System.Text.Json;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.Memory.Application;
using Neura.Modules.Memory.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Domain;
using Neura.Modules.ProviderIntegration.Infrastructure;
using Xunit;

namespace Neura.Tests;

public sealed class OrchestrationEngineTests
{
    [Fact]
    public async Task SuccessfulRealProviderResponse_CompletesOnceAndReturnsActualOutput()
    {
        var provider = new FakeProvider(ProviderKind.OpenAI, new AIResponse(
            Guid.NewGuid(), "Verified answer", new AITokenUsage(80, 30, 110, 10_000),
            TimeSpan.FromMilliseconds(42), 0.001m, true, null));
        var (engine, _, _) = CreateEngine();
        var mission = Mission.Create(Guid.NewGuid(), "M", "O", MissionMode.Real);
        var task = AgentTask.Create(mission.Id, "Complete the task", nameof(AgentCapability.Coding), 1);
        var agent = CreateAgent("Primary", AgentCapability.Coding);

        var result = await engine.RunTaskAsync(mission, task, agent, provider, null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Verified answer", result.Output);
        Assert.Equal(1, provider.CallCount);
        Assert.Equal(TaskStatus2.Completed, task.Status);
        Assert.Equal(MissionStatus.Completed, mission.Status);
    }

    [Fact]
    public async Task FailedProviderResponse_DoesNotGetReportedAsTaskCompletion()
    {
        var provider = new FakeProvider(ProviderKind.OpenAI, new AIResponse(
            Guid.NewGuid(), string.Empty, new AITokenUsage(0, 0, 0, 0),
            TimeSpan.Zero, 0m, false, "safe provider failure"));
        var (engine, _, _) = CreateEngine();
        var mission = Mission.Create(Guid.NewGuid(), "M", "O", MissionMode.Real);
        var task = AgentTask.Create(mission.Id, "Complete the task", nameof(AgentCapability.Coding), 1);
        var agent = CreateAgent("Primary", AgentCapability.Coding);

        var result = await engine.RunTaskAsync(mission, task, agent, provider, null, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Equal(TaskStatus2.Failed, task.Status);
        Assert.Equal(MissionStatus.Failed, mission.Status);
        Assert.Equal("Provider request failed; inspect protected server logs using the AI request ID.", result.Error);
        Assert.DoesNotContain("safe provider failure", result.Error!);
    }

    [Fact]
    public async Task ProjectMemory_IsRetrievedAndAddedToProviderRequestAsUntrustedContext()
    {
        var mission = Mission.Create(Guid.NewGuid(), "Checkout", "Use reliable database constraints", MissionMode.Real);
        var task = AgentTask.Create(mission.Id, "Implement schema validation", nameof(AgentCapability.Coding), 1);
        var memory = MemoryRecord.Create(mission.ProjectId, MemoryType.LongTermProject,
            "Keep order invariants in PostgreSQL constraints.", 0.9, 0.95, null, null,
            new[] { "database", "checkout" });
        var memories = new RecordingMemoryService(memory);
        var provider = new FakeProvider(ProviderKind.OpenAI, new AIResponse(
            Guid.NewGuid(), "Validated schema", new AITokenUsage(30, 10, 40, 10_000),
            TimeSpan.FromMilliseconds(20), 0m, true, null));
        var (engine, _, events) = CreateEngine(memories);
        var agent = CreateAgent("Primary", AgentCapability.Coding);

        var result = await engine.RunTaskAsync(mission, task, agent, provider, null, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(mission.ProjectId, memories.LastProjectId.GetValueOrDefault());
        Assert.Equal(5, memories.LastMaxRecords);
        Assert.Contains("Checkout", memories.LastQuery, StringComparison.Ordinal);
        Assert.NotNull(provider.LastRequest);
        Assert.Contains(provider.LastRequest!.Messages, message =>
            message.Content.Contains("NEURA PROJECT MEMORY — UNTRUSTED REFERENCE DATA", StringComparison.Ordinal) &&
            message.Content.Contains("Keep order invariants", StringComparison.Ordinal));
        var memoryEvent = events.PublishedEvents.Single(published =>
            published.EventType == NeuralEventTypes.ProjectMemoryRetrieved);
        var eventJson = JsonSerializer.Serialize(memoryEvent.Payload);
        Assert.Contains(memory.MemoryId.ToString(), eventJson, StringComparison.Ordinal);
        Assert.DoesNotContain(memory.Content, eventJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectMemory_RemainsUntrustedThroughContextHandoffToFallbackAgent()
    {
        var mission = Mission.Create(Guid.NewGuid(), "Checkout", "Preserve checkout invariants", MissionMode.Real);
        var task = AgentTask.Create(mission.Id, "Validate order persistence", nameof(AgentCapability.Coding), 1);
        var memory = MemoryRecord.Create(mission.ProjectId, MemoryType.LongTermProject,
            "Orders must remain idempotent across retries.", 0.9, 0.95, null, null,
            new[] { "checkout", "database" });
        var memories = new RecordingMemoryService(memory);
        var primaryProvider = new FakeProvider(ProviderKind.OpenAI, new AIResponse(
            Guid.NewGuid(), "Partial validation plan", new AITokenUsage(9_600, 0, 9_600, 10_000),
            TimeSpan.FromMilliseconds(20), 0m, true, null, IsTaskComplete: false));
        var fallbackProvider = new FakeProvider(ProviderKind.Anthropic, new AIResponse(
            Guid.NewGuid(), "Idempotency checks passed", new AITokenUsage(60, 20, 80, 10_000),
            TimeSpan.FromMilliseconds(20), 0m, true, null));
        var (engine, handoffs, _) = CreateEngine(memories);
        var primary = CreateAgent("Primary", AgentCapability.Coding);
        var fallback = CreateAgent("Fallback", AgentCapability.Coding);

        var result = await engine.RunTaskAsync(mission, task, primary, primaryProvider,
            fallback, fallbackProvider, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(fallback.Id, result.CompletedByAgentId);
        Assert.Single(handoffs.Records);
        var fallbackRequest = fallbackProvider.LastRequest
            ?? throw new InvalidOperationException("Fallback provider was not called.");
        Assert.Contains(fallbackRequest.Messages, message =>
            message.Content.Contains("NEURA PROJECT MEMORY — UNTRUSTED REFERENCE DATA", StringComparison.Ordinal) &&
            message.Content.Contains(memory.Content, StringComparison.Ordinal));
        Assert.Contains(fallbackRequest.Messages, message =>
            message.Content.Contains("UNTRUSTED STORED MEMORY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ContextExhaustion_PersistsValidatedHandoffAndCompletesOnFallback()
    {
        var (engine, handoffs, _) = CreateEngine();
        var mission = Mission.Create(Guid.NewGuid(), "M", "O", MissionMode.Simulation);
        var task = AgentTask.Create(mission.Id, "Continue checkout work", nameof(AgentCapability.Coding), 1);
        var primary = CreateAgent("Claude Simulator", AgentCapability.Coding);
        var fallback = CreateAgent("ChatGPT Simulator", AgentCapability.Coding);
        var firstProvider = new SimulationAIProvider("Claude Simulator", 100);
        var secondProvider = new SimulationAIProvider("ChatGPT Simulator", 100, completesTask: true);

        var result = await engine.RunTaskAsync(mission, task, primary, firstProvider,
            fallback, secondProvider, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(fallback.Id, result.CompletedByAgentId);
        Assert.Equal(MissionStatus.Completed, mission.Status);
        Assert.Single(handoffs.Records);
        Assert.Equal(HandoffReason.ContextExhaustion, handoffs.Records[0].Reason);
        Assert.Equal(HandoffState.Completed, handoffs.Records[0].State);
        Assert.Equal(fallback.Id, handoffs.Records[0].ToAgentId);
        Assert.Equal(3, handoffs.SaveCount);
    }

    private static (OrchestrationEngine Engine, RecordingHandoffSink Handoffs, RecordingEventPublisher Events) CreateEngine(IMemoryService? memoryService = null)
    {
        var events = new RecordingEventPublisher();
        var handoffs = new RecordingHandoffSink();
        var engine = new OrchestrationEngine(
            new ContextThresholdOptions(),
            new AgentRoutingEngine(),
            new ContextContinuityEngine(),
            events,
            handoffSink: handoffs,
            memoryService: memoryService);
        return (engine, handoffs, events);
    }

    private static Agent CreateAgent(string name, AgentCapability capability)
        => Agent.Create(name, "test", Guid.NewGuid(), "model-test", "Test Agent", 100_000, new[] { capability });

    private sealed class FakeProvider : IAIProvider
    {
        private readonly AIResponse _response;
        public FakeProvider(ProviderKind kind, AIResponse response) { Kind = kind; _response = response; }
        public int CallCount { get; private set; }
        public AIRequest? LastRequest { get; private set; }
        public ProviderKind Kind { get; }
        public bool IsSimulation => false;
        public Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_response with { AiRequestId = request.AiRequestId });
        }
        public Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken)
            => Task.FromResult(new AIModelCapabilities(modelId, 10_000, false, false, 0m, 0m));
        public Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken)
            => Task.FromResult(new ProviderHealth(true, "test", TimeSpan.Zero));
    }

    private sealed class RecordingMemoryService : IMemoryService
    {
        private readonly IReadOnlyList<MemoryRecord> _records;
        public RecordingMemoryService(params MemoryRecord[] records) => _records = records;
        public Guid? LastProjectId { get; private set; }
        public string LastQuery { get; private set; } = string.Empty;
        public int LastMaxRecords { get; private set; }

        public Task<IReadOnlyList<MemoryRecord>> RetrieveRelevantAsync(
            Guid projectId, string query, int maxRecords, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastProjectId = projectId;
            LastQuery = query;
            LastMaxRecords = maxRecords;
            return Task.FromResult(_records);
        }

        public Task<int> PurgeExpiredAsync(DateTime nowUtc, int maxRecords, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private sealed class RecordingEventPublisher : INeuralEventPublisher
    {
        public List<(string EventType, object Payload)> PublishedEvents { get; } = new();
        public Task PublishAsync(string eventType, object payload, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            PublishedEvents.Add((eventType, payload));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHandoffSink : IHandoffSink
    {
        public List<HandoffRecord> Records { get; } = new();
        public int SaveCount { get; private set; }
        public Task SaveAsync(HandoffRecord handoff, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (!Records.Contains(handoff)) Records.Add(handoff);
            return Task.CompletedTask;
        }
    }
}
