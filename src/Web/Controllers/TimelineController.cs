using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>
/// Execution Timeline (screen 12): replay a mission's recorded events.
/// Backed by ExecutionEvent rows written by PersistingNeuralEventPublisher
/// alongside every live SignalR broadcast.
/// </summary>
[Authorize(Policy = "ViewAuditLogs")]
public class TimelineController : Controller
{
    private readonly NeuraDbContext _db;
    public TimelineController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(Guid? missionId, CancellationToken ct)
    {
        var query = _db.ExecutionEvents.AsQueryable();
        if (missionId.HasValue)
            query = query.Where(e => e.MissionId == missionId.Value);

        var events = await query.OrderBy(e => e.TimestampUtc).Take(500).ToListAsync(ct);
        ViewBag.MissionId = missionId;
        ViewBag.Missions = await _db.Missions.Select(m => new { m.Id, m.Title }).ToListAsync(ct);
        return View(events);
    }
}
