using Neura.Modules.ContextManagement.Domain;

namespace Neura.Modules.Handoff.Domain;

public enum HandoffReason
{
    ContextExhaustion, ProviderFailure, AgentFailure, CapabilityMismatch,
    CostOptimization, LatencyOptimization, LowConfidence, ManualTransfer,
    PolicyRestriction, RateLimit, Timeout
}

public enum HandoffState { Pending, InProgress, Validating, Completed, Rejected, Failed }

public class HandoffRecord
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid MissionId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid FromAgentId { get; private set; }
    public Guid? ToAgentId { get; private set; }
    public HandoffReason Reason { get; private set; }
    public HandoffState State { get; private set; } = HandoffState.Pending;
    public Guid ContextPackageId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; private set; }
    public string? ValidationNotes { get; private set; }

    private HandoffRecord() { }

    public static HandoffRecord Create(Guid missionId, Guid taskId, Guid fromAgentId, HandoffReason reason, Guid contextPackageId)
        => new() { MissionId = missionId, TaskId = taskId, FromAgentId = fromAgentId, Reason = reason, ContextPackageId = contextPackageId };

    public void BeginTransfer() => State = HandoffState.InProgress;
    public void AssignReceiver(Guid agentId) { ToAgentId = agentId; State = HandoffState.Validating; }
    public void Validate(bool isValid, string? notes)
    {
        ValidationNotes = notes;
        State = isValid ? HandoffState.Completed : HandoffState.Rejected;
        if (isValid) CompletedAtUtc = DateTime.UtcNow;
    }
    public void Fail(string reason) { State = HandoffState.Failed; ValidationNotes = reason; }
}

public sealed record HandoffValidationResult(bool IsSufficient, List<string> MissingItems);

public static class HandoffValidator
{
    /// <summary>
    /// The receiving agent must not blindly continue: verify the package
    /// carries what the task needs before accepting the handoff.
    /// </summary>
    public static HandoffValidationResult Validate(ContextHandoffPackage package)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(package.CurrentTask)) missing.Add("currentTask");
        if (string.IsNullOrWhiteSpace(package.Status)) missing.Add("status");
        if (package.RemainingWork.Count == 0 && package.Status != "Completed") missing.Add("remainingWork");
        return new HandoffValidationResult(missing.Count == 0, missing);
    }
}
