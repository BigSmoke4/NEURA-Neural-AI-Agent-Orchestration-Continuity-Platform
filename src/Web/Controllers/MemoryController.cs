using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>Memory Center (screen 10): search and inspect memory.</summary>
[Authorize(Policy = "ViewMemory")]
public class MemoryController : Controller
{
    private readonly NeuraDbContext _db;
    public MemoryController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var query = _db.Memories.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(m => m.Content.Contains(q));
        var results = await query.OrderByDescending(m => m.CreatedAt).Take(100).ToListAsync(ct);
        ViewBag.Query = q;
        return View(results);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var record = await _db.Memories.FirstOrDefaultAsync(m => m.MemoryId == id, ct);
        if (record is null) return NotFound();
        _db.Memories.Remove(record);
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }
}
