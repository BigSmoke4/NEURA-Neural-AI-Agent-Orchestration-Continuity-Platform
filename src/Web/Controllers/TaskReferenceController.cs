using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Web.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Domain;
using Neura.Web.Security;

namespace Neura.Web.Controllers;

/// <summary>
/// Fetches a public http(s) reference and attaches it as explicitly
/// untrusted task material. The named HttpClient resolves and connects only
/// to globally routable IPs and refuses redirects to prevent SSRF.
/// </summary>
[Authorize(Policy = "ExecuteMission")]
[EnableRateLimiting(NeuraRateLimitPolicies.ProviderConfiguration)]
public class TaskReferenceController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuditLogger _audit;

    public TaskReferenceController(NeuraDbContext db, IHttpClientFactory httpClientFactory, IAuditLogger audit)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _audit = audit;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AttachFromUrl(Guid taskId, string url, CancellationToken ct)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null) return NotFound();

        var mission = await ResourceScope.Missions(_db.Missions, User)
            .FirstOrDefaultAsync(m => m.Id == task.MissionId, ct);
        if (mission is null) return NotFound();
        if (mission.Status != MissionStatus.Created)
            return Conflict("Reference material can only be attached before the mission is queued.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            (uri.Scheme == Uri.UriSchemeHttp && uri.Port != 80) ||
            (uri.Scheme == Uri.UriSchemeHttps && uri.Port != 443))
        {
            TempData["ReferenceError"] = "Provide a public absolute http(s) URL without embedded credentials or a custom port.";
            return RedirectToAction("Details", "Missions", new { id = mission.Id });
        }

        var client = _httpClientFactory.CreateClient("reference-fetch");
        string text;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("text/plain, text/html, application/json;q=0.9, */*;q=0.1");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            response.EnsureSuccessStatusCode();
            text = await response.Content.ReadAsStringAsync(ct);
            if (text.Length > 20_000)
                text = text[..20_000] + "\n[truncated by NEURA]";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Do not echo remote response bodies, internal addresses, or
            // exception details into a user-visible page.
            TempData["ReferenceError"] = "The reference could not be fetched. The URL must resolve to a public host and return a successful response without redirects.";
            return RedirectToAction("Details", "Missions", new { id = mission.Id });
        }

        // Keep only the origin in metadata/audit records. Query strings and
        // path segments can contain bearer/download tokens and are not needed
        // to explain where the untrusted material came from.
        var safeSource = uri.GetLeftPart(UriPartial.Authority);
        task.AttachReferenceMaterial(text, ContentTrustLevel.UntrustedExternalContent, safeSource);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ResourceScope.UserId(User), "ReferenceMaterialAttached", safeSource, "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return RedirectToAction("Details", "Missions", new { id = mission.Id });
    }
}
