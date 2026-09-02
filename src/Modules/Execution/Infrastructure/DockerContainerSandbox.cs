using System.Diagnostics;
using Neura.Modules.Execution.Domain;

namespace Neura.Modules.Execution.Infrastructure;

/// <summary>
/// Real container-isolated execution: shells out to the Docker CLI to run
/// submitted code in an ephemeral, network-disabled, resource-capped
/// container, then removes it. This is the "ContainerSandbox" option
/// named in section 62 of the spec — a genuine isolation boundary, not a
/// mock — but it still requires the host running NEURA to have Docker
/// available and its socket reachable, and it is not wired into any
/// automatic code path: something must call ExecuteAsync explicitly.
/// </summary>
public sealed class DockerContainerSandbox : IExecutionSandbox
{
    private static readonly Dictionary<string, string> LanguageImages = new()
    {
        ["python"] = "python:3.12-slim",
        ["node"] = "node:20-slim",
        ["dotnet"] = "mcr.microsoft.com/dotnet/sdk:8.0"
    };

    public async Task<SandboxExecutionResult> ExecuteAsync(SandboxExecutionRequest request, CancellationToken ct = default)
    {
        if (!LanguageImages.TryGetValue(request.Language.ToLowerInvariant(), out var image))
            return new SandboxExecutionResult(false, string.Empty, $"Unsupported sandbox language: {request.Language}", TimeSpan.Zero);

        var tempDir = Path.Combine(Path.GetTempPath(), "neura-sandbox-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var (scriptFile, command) = request.Language.ToLowerInvariant() switch
            {
                "python" => ("script.py", "python /workspace/script.py"),
                "node" => ("script.js", "node /workspace/script.js"),
                "dotnet" => ("Program.cs", "dotnet run --project /workspace"),
                _ => throw new InvalidOperationException()
            };
            await File.WriteAllTextAsync(Path.Combine(tempDir, scriptFile), request.Code, ct);

            var args = string.Join(' ', new[]
            {
                "run", "--rm",
                "--network", "none",           // no network access
                "--memory", "256m",
                "--cpus", "0.5",
                "--pids-limit", "64",
                "--cap-drop", "ALL",
                "--security-opt", "no-new-privileges:true",
                "--read-only",
                "--tmpfs", "/tmp:rw,noexec,nosuid,size=64m",
                "-v", $"{tempDir}:/workspace:ro",
                image, "sh", "-c", $"\"{command}\""
            });

            var psi = new ProcessStartInfo("docker", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(request.Timeout);

            var sw = Stopwatch.StartNew();
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start docker process.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);
            sw.Stop();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            return new SandboxExecutionResult(process.ExitCode == 0, stdout, string.IsNullOrEmpty(stderr) ? null : stderr, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return new SandboxExecutionResult(false, string.Empty, "Execution timed out or was cancelled.", request.Timeout);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort cleanup */ }
        }
    }
}
