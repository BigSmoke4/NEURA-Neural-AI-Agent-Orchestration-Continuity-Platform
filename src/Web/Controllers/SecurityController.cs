using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>
/// Security Center (screen 15): credentials, permissions, audit events.
/// Shows connected-provider credential *references* only (never raw
/// secrets — see docs/SECURITY.md) plus the full audit trail.
/// </summary>
[Authorize(Policy = "ViewCredentials")]
public class SecurityController : Controller
{
    private readonly NeuraDbContext _db;
    public SecurityController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Providers = await _db.ProviderAccounts.ToListAsync(ct);
        ViewBag.AuditLog = await _db.AuditLogs.OrderByDescending(a => a.TimestampUtc).Take(200).ToListAsync(ct);
        return View();
    }
}
