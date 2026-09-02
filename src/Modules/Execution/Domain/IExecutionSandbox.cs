namespace Neura.Modules.Execution.Domain;

public sealed record SandboxExecutionRequest(string Language, string Code, TimeSpan Timeout);
public sealed record SandboxExecutionResult(bool Success, string Output, string? Error, TimeSpan Duration);

/// <summary>
/// Section 62: abstraction for running AI-generated code in isolation.
/// No implementation of this interface is ever invoked automatically —
/// wiring a sandbox in does not by itself mean agent output gets
/// executed; that decision belongs to whatever calls IExecutionSandbox
/// explicitly, and none of NEURA's current controllers or the
/// orchestration engine do so.
/// </summary>
public interface IExecutionSandbox
{
    Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken ct = default);
}
