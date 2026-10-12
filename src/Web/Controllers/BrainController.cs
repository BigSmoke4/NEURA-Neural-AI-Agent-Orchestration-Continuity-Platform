using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Infrastructure;
using Neura.Modules.Observability.Domain;
using Neura.Web.Security;

namespace Neura.Web.Controllers;

/// <summary>The Brain dashboard's deterministic, persisted handoff demonstration.</summary>
[Authorize]
public class BrainController : Controller
{
    private readonly OrchestrationEngine _orchestrator;
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ILogger<BrainController> _logger;

    public BrainController(
        OrchestrationEngine orchestrator,
        NeuraDbContext db,
        IAuditLogger audit,
        ILogger<BrainController> logger)
    {
        _orchestrator = orchestrator;
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public IActionResult Index() => View();

    /// <summary>
    /// Runs the deterministic Claude Simulator → context exhaustion →
    /// handoff → ChatGPT Simulator continuation. Simulation results are
    /// explicitly labeled and the mission/events/packages are persisted,
    /// so the same run can be inspected after the live animation ends.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "ExecuteMission")]
    public async Task<IActionResult> RunSimulationDemo(CancellationToken ct)
    {
        var ownerUserId = ResourceScope.UserId(User);
        if (!ownerUserId.HasValue)
            return Unauthorized(new { success = false, error = "The authenticated user identity is invalid." });

        var claude = Agent.Create("Claude Simulator", "Deterministic context-exhaustion demo agent", Guid.NewGuid(),
            "claude-simulator", "Coding Agent", 100, new[] { AgentCapability.Coding });
        var gpt = Agent.Create("ChatGPT Simulator", "Deterministic continuation demo agent", Guid.NewGuid(),
            "chatgpt-simulator", "Reasoning Agent", 100, new[] { AgentCapability.Coding, AgentCapability.Reasoning });
        var mission = Mission.Create(Guid.NewGuid(), "Improve the checkout system",
            "Demonstrate structured context continuity and handoff (Simulation Mode).",
            MissionMode.Simulation, ownerUserId);
        var task = AgentTask.Create(mission.Id, "Implement checkout performance fix",
            nameof(AgentCapability.Coding), 1);
        mission.AddTask(task);

        var claudeSimulator = new SimulationAIProvider("Claude Simulator", 100);
        var chatGptSimulator = new SimulationAIProvider("ChatGPT Simulator", 100, completesTask: true);

        _db.Missions.Add(mission);
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ownerUserId, "SimulationMissionCreated", mission.Id.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        try
        {
            var result = await _orchestrator.RunTaskAsync(mission, task, claude, claudeSimulator,
                gpt, chatGptSimulator, ct);
            await _db.SaveChangesAsync(ct);

            return Json(new
            {
                result.Success,
                result.Error,
                result.Output,
                Mode = "SIMULATION",
                StartedBy = claude.Name,
                CompletedBy = result.CompletedByAgentId == gpt.Id ? gpt.Name : claude.Name,
                MissionId = mission.Id,
                TaskId = task.Id
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            mission.SetStatus(MissionStatus.Cancelled);
            await _db.SaveChangesAsync(CancellationToken.None);
            return StatusCode(StatusCodes.Status408RequestTimeout,
                new { success = false, error = "Simulation cancelled." });
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid();
            _logger.LogError(ex, "Simulation mission {MissionId} failed. CorrelationId={CorrelationId}", mission.Id, correlationId);
            mission.SetStatus(MissionStatus.Failed);
            await _db.SaveChangesAsync(CancellationToken.None);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { success = false, error = "Simulation failed. Inspect the execution timeline.", correlationId });
        }
    }
}
