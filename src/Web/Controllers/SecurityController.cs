using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>
/// Security Center (screen 15): provider-account metadata and audit events.
/// Secret material and protected credential payloads are never included in
/// the view model.
/// </summary>
[Authorize(Policy = "ViewCredentials")]
public class SecurityController : Controller
{
    private readonly NeuraDbContext _db;
    public SecurityController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Providers = await _db.ProviderAccounts
            .OrderBy(p => p.Kind)
            .ThenBy(p => p.DisplayName)
            .Select(p => new ProviderAccountSummary(p.Kind, p.DisplayName, p.State,
                p.LastHealthCheckUtc, p.LastHealthMessage))
            .ToListAsync(ct);
        ViewBag.AuditLog = await _db.AuditLogs.OrderByDescending(a => a.TimestampUtc).Take(200).ToListAsync(ct);
        return View();
    }
}
