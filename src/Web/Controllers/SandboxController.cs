using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Modules.Execution.Domain;
using Neura.Modules.Observability.Domain;

namespace Neura.Web.Controllers;

/// <summary>
/// The one real caller of IExecutionSandbox in this codebase: an
/// Admin-only manual test screen. This is deliberately NOT wired into
/// the orchestration engine or any agent's automatic output path — an
/// agent's generated code is never executed without a human explicitly
/// invoking this action, which is the "own review step" gap called out
/// in the README. Every run is audited.
/// </summary>
[Authorize(Policy = "ManageSystem")]
[EnableRateLimiting("provider-config")]
public class SandboxController : Controller
{
    private readonly IExecutionSandbox _sandbox;
    private readonly IAuditLogger _audit;

    public SandboxController(IExecutionSandbox sandbox, IAuditLogger audit)
    {
        _sandbox = sandbox;
        _audit = audit;
    }

    public IActionResult Index() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Run(string language, string code, CancellationToken ct)
    {
        var request = new SandboxExecutionRequest(language, code, TimeSpan.FromSeconds(15));
        var result = await _sandbox.ExecuteAsync(request, ct);

        await _audit.LogAsync(null, "SandboxExecuted", language, result.Success ? "Success" : "Failed",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        ViewBag.Result = result;
        ViewBag.Language = language;
        ViewBag.Code = code;
        return View(nameof(Index));
    }
}
