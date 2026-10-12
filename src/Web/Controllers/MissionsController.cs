using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Web.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;
using Neura.Web.Security;
using Neura.Web.Validation;

namespace Neura.Web.Controllers;

/// <summary>Mission Control: create, queue, and inspect owned missions.</summary>
[EnableRateLimiting(NeuraRateLimitPolicies.MissionCreation)]
[Authorize(Policy = "ExecuteMission")]
public class MissionsController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IValidator<CreateMissionRequest> _validator;

    public MissionsController(NeuraDbContext db, IAuditLogger audit, IValidator<CreateMissionRequest> validator)
    {
        _db = db;
        _audit = audit;
        _validator = validator;
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var missions = await VisibleMissions()
            .OrderByDescending(m => m.CreatedAtUtc)
            .Take(50)
            .ToListAsync(ct);
        await LoadProjectChoicesAsync(ct);
        return View(missions);
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var mission = await VisibleMissions().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mission is null) return NotFound();

        var tasks = await _db.Tasks
            .Where(t => t.MissionId == id)
            .OrderBy(t => t.Order)
            .ToListAsync(ct);

        var completedEvent = await _db.ExecutionEvents
            .Where(e => e.MissionId == id && e.EventType == NeuralEventTypes.TaskCompleted)
            .OrderByDescending(e => e.TimestampUtc)
            .FirstOrDefaultAsync(ct);
        ViewBag.Output = TryReadOutput(completedEvent?.PayloadJson);
        ViewBag.Mission = mission;
        return View(tasks);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string title, string objective, string mode, Guid? projectId, CancellationToken ct)
    {
        var request = new CreateMissionRequest { Title = title, Objective = objective, Mode = mode };
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(string.Empty, error.ErrorMessage);

            var missions = await VisibleMissions()
                .OrderByDescending(m => m.CreatedAtUtc)
                .Take(50)
                .ToListAsync(ct);
            await LoadProjectChoicesAsync(ct);
            return View(nameof(Index), missions);
        }

        var ownerUserId = ResourceScope.UserId(User);
        if (!ownerUserId.HasValue)
            return Unauthorized();

        var selectedProjectId = Guid.NewGuid();
        if (projectId.HasValue)
        {
            var isOwnedProject = await ResourceScope.ProjectIds(_db.Missions, User)
                .AnyAsync(existingProjectId => existingProjectId == projectId.Value, ct);
            if (!isOwnedProject)
                return NotFound();
            selectedProjectId = projectId.Value;
        }

        var missionMode = request.Mode == "Real" ? MissionMode.Real : MissionMode.Simulation;
        var mission = Mission.Create(selectedProjectId, request.Title, request.Objective, missionMode, ownerUserId);
        _db.Missions.Add(mission);

        var task = AgentTask.Create(mission.Id, request.Title,
            nameof(Neura.Modules.AgentManagement.Domain.AgentCapability.Coding), 1);
        _db.Tasks.Add(task);

        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ownerUserId, "MissionCreated", mission.Id.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        // Queueing is a separate user action. The Queued status is persisted
        // before the hosted worker claims it, so accepted missions survive a
        // process restart and repeated POSTs cannot enqueue another copy.
        return RedirectToAction(nameof(Details), new { id = mission.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var mission = await VisibleMissions().FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mission is null) return NotFound();
        if (mission.Status != MissionStatus.Created)
            return Conflict("This mission has already been queued or has started.");

        try
        {
            mission.Queue();
            await _db.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException)
        {
            return Conflict("This mission has already been queued or has started.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("This mission was queued by another request.");
        }

        var userId = ResourceScope.UserId(User);
        await _audit.LogAsync(userId, "MissionStarted", mission.Id.ToString(), "Queued",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task LoadProjectChoicesAsync(CancellationToken ct)
    {
        var recentProjects = await VisibleMissions()
            .OrderByDescending(mission => mission.CreatedAtUtc)
            .Select(mission => new { mission.ProjectId, mission.Title })
            .Take(1_000)
            .ToListAsync(ct);

        ViewBag.ProjectChoices = recentProjects
            .GroupBy(project => project.ProjectId)
            .Select(group => new { ProjectId = group.Key, Label = group.First().Title })
            .OrderBy(project => project.Label)
            .Take(100)
            .ToList();
    }

    private IQueryable<Mission> VisibleMissions()
        => ResourceScope.Missions(_db.Missions, User);

    private static string? TryReadOutput(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("Output", out var output) && output.ValueKind == JsonValueKind.String)
                return output.GetString();
            if (document.RootElement.TryGetProperty("output", out output) && output.ValueKind == JsonValueKind.String)
                return output.GetString();
        }
        catch (JsonException)
        {
            // Older timeline events may not use the current structured payload.
        }

        return null;
    }
}
