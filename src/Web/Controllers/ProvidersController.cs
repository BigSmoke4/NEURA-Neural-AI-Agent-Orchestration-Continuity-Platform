using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Infrastructure.Security;
using Neura.Modules.Observability.Domain;
using Neura.Modules.ProviderIntegration.Domain;
using Neura.Web.Validation;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>
/// Provider Management (screen 7). The submitted API key is encrypted at
/// rest via ICredentialProtector before it's ever written to the
/// database — see docs/SECURITY.md. The raw key is never logged and
/// never rendered back to any view.
/// </summary>
[EnableRateLimiting("provider-config")]
[Authorize(Policy = "ManageProviders")]
public class ProvidersController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly ICredentialProtector _credentialProtector;
    private readonly IValidator<ConnectProviderRequest> _validator;

    public ProvidersController(NeuraDbContext db, IAuditLogger audit, ICredentialProtector credentialProtector, IValidator<ConnectProviderRequest> validator)
    {
        _db = db;
        _audit = audit;
        _credentialProtector = credentialProtector;
        _validator = validator;
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var providers = await _db.ProviderAccounts.ToListAsync(ct);
        return View(providers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Connect(Guid userId, string kind, string displayName, string apiKey, CancellationToken ct)
    {
        var request = new ConnectProviderRequest { UserId = userId, Kind = kind, DisplayName = displayName, ApiKey = apiKey };
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(string.Empty, error.ErrorMessage);
            return RedirectToAction(nameof(Index));
        }

        var providerKind = Enum.Parse<ProviderKind>(kind);
        var encryptedRef = _credentialProtector.Protect(apiKey);
        var account = AIProviderAccount.Connect(userId, providerKind, displayName, encryptedRef);
        _db.ProviderAccounts.Add(account);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(userId, "ProviderConnected", account.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
    {
        var account = await _db.ProviderAccounts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (account is null) return NotFound();
        account.Disconnect();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(account.UserId, "ProviderDisconnected", account.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Section 37: credential rotation. Re-encrypts the account with a
    /// newly supplied API key (revoked/reissued on the vendor's side by
    /// the operator first) — the old encrypted value is fully replaced,
    /// never retained, and the rotation itself is audited.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Rotate(Guid id, string newApiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(newApiKey) || newApiKey.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "New API key looks too short to be valid.");
            return RedirectToAction(nameof(Index));
        }

        var account = await _db.ProviderAccounts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (account is null) return NotFound();

        var encrypted = _credentialProtector.Protect(newApiKey);
        account.Rotate(encrypted);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(account.UserId, "CredentialRotated", account.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }
}
