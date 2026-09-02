using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>Handoff Center (screen 8): historical and active handoffs.</summary>
[Authorize(Policy = "ViewMemory")]
public class HandoffsController : Controller
{
    private readonly NeuraDbContext _db;
    public HandoffsController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var handoffs = await _db.Handoffs.OrderByDescending(h => h.CreatedAtUtc).Take(100).ToListAsync(ct);
        return View(handoffs);
    }
}
