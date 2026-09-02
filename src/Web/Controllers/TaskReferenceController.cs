using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Observability.Domain;

namespace Neura.Web.Controllers;

/// <summary>
/// The real untrusted-content producer the trust-labeling mechanism was
/// missing: fetches a URL the user supplies and attaches its raw text to
/// a task via AgentTask.AttachReferenceMaterial(..., UntrustedExternalContent, ...).
/// This is genuinely external, unsanitized content — exactly the case
/// ContentTrustLevel exists to guard — flowing into
/// OrchestrationEngine.BuildTrustSeparatedMessages the next time that
/// task runs, where it will be rendered as clearly-delimited,
/// non-authoritative reference material rather than merged into the
/// instruction stream.
/// </summary>
[Authorize(Policy = "ExecuteMission")]
[EnableRateLimiting("provider-config")]
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

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            ModelState.AddModelError(string.Empty, "Provide a valid absolute http(s) URL.");
            return RedirectToAction("Index", "Missions");
        }

        var client = _httpClientFactory.CreateClient("reference-fetch");
        string text;
        try
        {
            text = await client.GetStringAsync(uri, ct);
            if (text.Length > 20_000) text = text[..20_000] + "\n[truncated]";
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, $"Could not fetch that URL: {ex.Message}");
            return RedirectToAction("Index", "Missions");
        }

        // The whole point: this content is fetched from the open web, so
        // it is labeled UntrustedExternalContent, never anything higher.
        task.AttachReferenceMaterial(text, ContentTrustLevel.UntrustedExternalContent, uri.ToString());
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(null, "ReferenceMaterialAttached", uri.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        return RedirectToAction("Index", "Missions");
    }
}
