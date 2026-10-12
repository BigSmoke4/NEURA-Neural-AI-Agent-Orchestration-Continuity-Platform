using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Web.Security;

namespace Neura.Web.Controllers;

/// <summary>Section 79: in-app notifications for context warnings, handoffs, etc.</summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly NeuraDbContext _db;
    public NotificationsController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var notifications = await ResourceScope.Notifications(_db.Notifications, _db, User)
            .OrderByDescending(n => n.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);
        return View(notifications);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "ManageNotifications")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var notification = await ResourceScope.Notifications(_db.Notifications, _db, User)
            .FirstOrDefaultAsync(n => n.Id == id, ct);
        if (notification is null) return NotFound();
        notification.MarkRead();
        await _db.SaveChangesAsync(ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> UnreadCount(CancellationToken ct)
        => Json(new { count = await ResourceScope.Notifications(_db.Notifications, _db, User)
            .CountAsync(n => !n.IsRead, ct) });
}
