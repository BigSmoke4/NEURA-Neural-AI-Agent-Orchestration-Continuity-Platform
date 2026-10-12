using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Observability.Domain;
using Neura.Web.Security;

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
        if (!AssignableRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Unknown role.");
        role = AssignableRoles.First(allowed => allowed.Equals(role, StringComparison.OrdinalIgnoreCase));

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();

        var actorUserId = ResourceScope.UserId(User);
        var correlationId = Guid.NewGuid();
        var result = await _userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            await _audit.LogAsync(actorUserId, "RoleAssigned", $"{user.Id}; role={role}",
                "Failed: " + string.Join(",", result.Errors.Select(error => error.Code)), correlationId,
                HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            TempData["UserError"] = "The role could not be assigned.";
            return RedirectToAction(nameof(Index));
        }

        await _audit.LogAsync(actorUserId, "RoleAssigned", $"{user.Id}; role={role}", "Success",
            correlationId, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveRole(Guid userId, string role, CancellationToken ct)
    {
        if (!AssignableRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            return BadRequest("Unknown role.");
        role = AssignableRoles.First(allowed => allowed.Equals(role, StringComparison.OrdinalIgnoreCase));

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound();

        var actorUserId = ResourceScope.UserId(User);
        var correlationId = Guid.NewGuid();
        if (role == "Admin" && await _userManager.IsInRoleAsync(user, "Admin"))
        {
            var adminsRemaining = (await _userManager.GetUsersInRoleAsync("Admin"))
                .Count(admin => admin.Id != user.Id);
            if (adminsRemaining == 0)
            {
                await _audit.LogAsync(actorUserId, "RoleRemoved", $"{user.Id}; role={role}",
                    "Rejected: last Admin would be removed", correlationId,
                    HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
                TempData["UserError"] = "At least one Admin account must remain assigned.";
                return RedirectToAction(nameof(Index));
            }
        }

        var result = await _userManager.RemoveFromRoleAsync(user, role);
        if (!result.Succeeded)
        {
            await _audit.LogAsync(actorUserId, "RoleRemoved", $"{user.Id}; role={role}",
                "Failed: " + string.Join(",", result.Errors.Select(error => error.Code)), correlationId,
                HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
            TempData["UserError"] = "The role could not be removed.";
            return RedirectToAction(nameof(Index));
        }

        await _audit.LogAsync(actorUserId, "RoleRemoved", $"{user.Id}; role={role}", "Success",
            correlationId, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }
}
