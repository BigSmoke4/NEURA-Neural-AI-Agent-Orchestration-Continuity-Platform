using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>Cost Center (screen 14): provider/model/agent/mission cost breakdown.</summary>
[Authorize(Policy = "ViewAuditLogs")]
public class CostController : Controller
{
    private readonly NeuraDbContext _db;
    public CostController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var records = await _db.CostRecords.OrderByDescending(c => c.CreatedAtUtc).Take(200).ToListAsync(ct);
        ViewBag.TotalCost = records.Sum(r => r.EstimatedCost);
        ViewBag.ByProvider = records.GroupBy(r => r.Provider)
            .Select(g => new { Provider = g.Key, Total = g.Sum(r => r.EstimatedCost) })
            .ToList();
        return View(records);
    }
}
