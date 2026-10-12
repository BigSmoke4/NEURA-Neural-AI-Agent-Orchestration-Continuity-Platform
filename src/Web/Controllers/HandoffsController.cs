using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Web.Security;
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
        var handoffs = await ResourceScope.Handoffs(_db.Handoffs, _db, User)
            .OrderByDescending(h => h.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return View(handoffs);
    }
}
