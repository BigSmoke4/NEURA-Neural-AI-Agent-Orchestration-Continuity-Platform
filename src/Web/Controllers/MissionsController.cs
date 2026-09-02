using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Web.Validation;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>Mission Control (screen 3/28): create and monitor missions.</summary>
[EnableRateLimiting("mission-creation")]
[Authorize(Policy = "ExecuteMission")]
public class MissionsController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IMissionQueue _queue;
    private readonly IAuditLogger _audit;
    private readonly IValidator<CreateMissionRequest> _validator;

    public MissionsController(NeuraDbContext db, IMissionQueue queue, IAuditLogger audit, IValidator<CreateMissionRequest> validator)
    {
        _db = db;
        _queue = queue;
        _audit = audit;
        _validator = validator;
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var missions = await _db.Missions.OrderByDescending(m => m.CreatedAtUtc).Take(50).ToListAsync(ct);
        return View(missions);
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var mission = await _db.Missions.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mission is null) return NotFound();
        var tasks = await _db.Tasks.Where(t => t.MissionId == id).OrderBy(t => t.Order).ToListAsync(ct);
        ViewBag.Mission = mission;
        return View(tasks);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string title, string objective, string mode, CancellationToken ct)
    {
        var request = new CreateMissionRequest { Title = title, Objective = objective, Mode = mode };
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(string.Empty, error.ErrorMessage);
            var missions = await _db.Missions.OrderByDescending(m => m.CreatedAtUtc).Take(50).ToListAsync(ct);
            return View(nameof(Index), missions);
        }

        var missionMode = request.Mode == "Real" ? MissionMode.Real : MissionMode.Simulation;
        var ownerUserId = Guid.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsedUserId)
            ? parsedUserId
            : (Guid?)null;
        var mission = Mission.Create(Guid.NewGuid(), request.Title, request.Objective, missionMode, ownerUserId);
        _db.Missions.Add(mission);

        var task = Neura.Modules.Orchestration.Domain.AgentTask.Create(mission.Id, request.Title,
            nameof(Neura.Modules.AgentManagement.Domain.AgentCapability.Coding), 1);
        _db.Tasks.Add(task);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(null, "MissionCreated", mission.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        // Mission is created and its task persisted, but NOT queued yet —
        // this gives the operator a chance to attach reference material
        // (Details screen) before Start enqueues it for the background
        // MissionWorker. This is what lets TaskReferenceController's
        // fetched content actually reach the orchestration run.
        return RedirectToAction(nameof(Details), new { id = mission.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var mission = await _db.Missions.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mission is null) return NotFound();

        await _queue.EnqueueAsync(new QueuedMission(mission.Id, mission.Title, mission.Objective, mission.Mode), ct);
        await _audit.LogAsync(null, "MissionStarted", mission.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return RedirectToAction(nameof(Details), new { id });
    }
}
