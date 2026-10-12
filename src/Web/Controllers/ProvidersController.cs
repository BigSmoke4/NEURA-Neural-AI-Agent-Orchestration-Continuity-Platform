using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Web.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Infrastructure.Security;
using Neura.Modules.Observability.Domain;
using Neura.Modules.ProviderIntegration.Domain;
using Neura.Web.Security;
using Neura.Web.Validation;

namespace Neura.Web.Controllers;

/// <summary>
/// Provider management for the signed-in user. Credential ownership is
/// derived from the authenticated principal, never from a client-supplied
/// user id; every read and mutation is owner-scoped.
/// </summary>
[EnableRateLimiting(NeuraRateLimitPolicies.ProviderConfiguration)]
[Authorize(Policy = "ManageProviders")]
public class ProvidersController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IValidator<ConnectProviderRequest> _validator;

    public ProvidersController(
        NeuraDbContext db,
        IAuditLogger audit,
        ICredentialProtector credentialProtector,
        IValidator<ConnectProviderRequest> validator)
    {
        _db = db;
        _audit = audit;
        _credentialProtector = credentialProtector;
        _validator = validator;
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var providers = await ResourceScope.OwnProviders(_db.ProviderAccounts, User)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
        return View(providers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(string kind, string displayName, string apiKey, CancellationToken ct)
    {
        var request = new ConnectProviderRequest { Kind = kind, DisplayName = displayName, ApiKey = apiKey };
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            TempData["ProviderError"] = string.Join(" ", validation.Errors.Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        var userId = ResourceScope.UserId(User);
        if (!userId.HasValue)
            return Unauthorized();

        var providerKind = Enum.Parse<ProviderKind>(kind, ignoreCase: true);
        var encryptedRef = _credentialProtector.Protect(apiKey);
        var account = AIProviderAccount.Connect(userId.Value, providerKind, displayName, encryptedRef);
        _db.ProviderAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(userId, "ProviderConnected", account.Id.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var account = await ResourceScope.OwnProviders(_db.ProviderAccounts, User)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (account is null) return NotFound();

        account.Disconnect();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(account.UserId, "ProviderDisconnected", account.Id.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Credential rotation replaces the old protected value and audits the
    /// operation. The raw key is never rendered or written to application logs.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rotate(Guid id, string newApiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newApiKey) || newApiKey.Length < 8)
        {
            TempData["ProviderError"] = "New API key looks too short to be valid.";
            return RedirectToAction(nameof(Index));
        }

        var account = await ResourceScope.OwnProviders(_db.ProviderAccounts, User)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (account is null) return NotFound();

        account.Rotate(_credentialProtector.Protect(newApiKey));
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(account.UserId, "CredentialRotated", account.Id.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }
}
