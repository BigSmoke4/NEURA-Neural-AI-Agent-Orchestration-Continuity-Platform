using Microsoft.AspNetCore.Mvc;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Infrastructure;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>
/// The Brain Dashboard — the primary neural visualization and, per the
/// product principle, the primary operational interface for the whole
/// orchestration system.
/// </summary>
[Authorize]
public class BrainController : Controller
{
    private readonly OrchestrationEngine _orchestrator;
    private readonly SimulationAIProvider _claudeSim;
    private readonly SimulationAIProvider _gptSim;

    public BrainController(OrchestrationEngine orchestrator)
    {
        _orchestrator = orchestrator;
        _claudeSim = new SimulationAIProvider("Claude Simulator", 100);
        _gptSim = new SimulationAIProvider("ChatGPT Simulator", 100);
    }

    public IActionResult Index() => View();

    /// <summary>
    /// Runs the deterministic Section-67 demonstration scenario:
    /// Claude Simulator ramps 30/55/72/88/96% context usage, crosses the
    /// exhaustion threshold, hands off to ChatGPT Simulator, which
    /// validates the package and continues to completion. All steps are
    /// broadcast live over SignalR to the Brain dashboard.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RunSimulationDemo(CancellationToken ct)
    {
        var claude = Agent.Create("Claude Simulator", "Simulated coding agent", Guid.NewGuid(),
            "claude-simulator", "Coding Agent", 100, new[] { AgentCapability.Coding });
        var gpt = Agent.Create("ChatGPT Simulator", "Simulated reasoning agent", Guid.NewGuid(),
            "gpt-simulator", "Reasoning Agent", 100, new[] { AgentCapability.Reasoning });

        var mission = Mission.Create(Guid.NewGuid(), "Improve the checkout system", "Demo mission (Simulation Mode)", MissionMode.Simulation);
        var task = AgentTask.Create(mission.Id, "Implement checkout performance fix", nameof(AgentCapability.Coding), 1);
        mission.AddTask(task);

        _claudeSim.Reset();
        _gptSim.Reset();

        try
        {
            var result = await _orchestrator.RunTaskAsync(mission, task, claude, _claudeSim, gpt, _gptSim, ct);

            return Json(new
        {
            result.Success,
            result.Error,
            StartedBy = claude.Name,
            CompletedBy = result.CompletedByAgentId == gpt.Id ? gpt.Name : claude.Name,
            MissionId = mission.Id,
            TaskId = task.Id
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(StatusCodes.Status408RequestTimeout, new { success = false, error = "Simulation cancelled." });
        }
        catch (Exception ex)
        {
            // Return JSON even on failure so the dashboard never tries to parse an HTML error page.
            return StatusCode(StatusCodes.Status500InternalServerError, new { success = false, error = ex.Message });
        }
    }
}
