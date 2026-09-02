using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;

namespace Neura.Web.Controllers;

/// <summary>
/// Admin-only user management: assign/remove roles. Closes the "no
/// admin UI for role assignment" gap — promoting a user to Admin,
/// Operator, or Auditor no longer requires a direct database edit.
/// </summary>
[Authorize(Policy = "ManageSystem")]
public class UsersController : Controller
{
    private static readonly string[] AssignableRoles = { "Admin", "Operator", "User", "Auditor" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;

    public UsersController(UserManager<ApplicationUser> userManager, NeuraDbContext db, IAuditLogger audit)
    {
        _userManager = userManager;
        _db = db;
        _audit = audit;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var users = await _db.Users.ToListAsync(ct);
        var userRoles = new Dictionary<Guid, IList<string>>();
        foreach (var user in users)
            userRoles[user.Id] = await _userManager.GetRolesAsync(user);

        ViewBag.UserRoles = userRoles;
        ViewBag.AssignableRoles = AssignableRoles;
        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignRole(Guid userId, string role, CancellationToken ct)
    {
        if (!AssignableRoles.Contains(role)) return BadRequest("Unknown role.");

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();

        await _userManager.AddToRoleAsync(user, role);
        await _audit.LogAsync(userId, "RoleAssigned", role, "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRole(Guid userId, string role, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();

        await _userManager.RemoveFromRoleAsync(user, role);
        await _audit.LogAsync(userId, "RoleRemoved", role, "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }
}
