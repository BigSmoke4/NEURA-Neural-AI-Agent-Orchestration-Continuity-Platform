using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>
/// Observability (screen 13): surfaces audit/health data already
/// captured. Full OpenTelemetry trace/metric export (section 33) is
/// configured for ASP.NET Core request instrumentation in Program.cs;
/// a dedicated metrics backend (Prometheus/Grafana/Jaeger) is not wired
/// up here — this screen reads what's already persisted in Postgres.
/// </summary>
[Authorize(Policy = "ViewAuditLogs")]
public class ObservabilityController : Controller
{
    private readonly NeuraDbContext _db;
    public ObservabilityController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.RecentAudit = await _db.AuditLogs.OrderByDescending(a => a.TimestampUtc).Take(50).ToListAsync(ct);
        ViewBag.MissionCount = await _db.Missions.CountAsync(ct);
        ViewBag.AgentCount = await _db.Agents.CountAsync(ct);
        ViewBag.HandoffCount = await _db.Handoffs.CountAsync(ct);
        return View();
    }
}
