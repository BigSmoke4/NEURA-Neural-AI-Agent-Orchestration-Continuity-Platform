using Neura.Shared;

namespace Neura.Modules.Orchestration.Domain;

public record MissionStarted(Guid MissionId) : DomainEvent;
public record TaskAssigned(Guid TaskId, Guid AgentId) : DomainEvent;
public record AgentExecutionStarted(Guid TaskId, Guid AgentId) : DomainEvent;
public record ContextThresholdReached(Guid AgentId, Guid TaskId, double UsageRatio, string Level) : DomainEvent;
public record HandoffRequested(Guid MissionId, Guid TaskId, Guid FromAgentId, string Reason) : DomainEvent;
public record HandoffCompleted(Guid MissionId, Guid TaskId, Guid FromAgentId, Guid ToAgentId) : DomainEvent;
public record TaskCompleted(Guid TaskId) : DomainEvent;
public record MissionCompleted(Guid MissionId) : DomainEvent;
