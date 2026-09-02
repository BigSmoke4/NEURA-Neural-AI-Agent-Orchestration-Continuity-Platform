using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Web.Validation;
using Microsoft.AspNetCore.Authorization;

namespace Neura.Web.Controllers;

/// <summary>Agent Network + Agent Details (screens 5 & 6).</summary>
[Authorize(Policy = "ManageAgents")]
public class AgentsController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;
    private readonly IValidator<CreateAgentRequest> _validator;

    public AgentsController(NeuraDbContext db, IAuditLogger audit, IValidator<CreateAgentRequest> validator)
    {
        _db = db;
        _audit = audit;
        _validator = validator;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var agents = await _db.Agents.ToListAsync(ct);
        return View(agents);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return NotFound();
        return View(agent);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("provider-config")]
    public async Task<IActionResult> Create(string name, string description, Guid providerAccountId,
        string modelId, string role, int contextCapacityTokens, string[] capabilities, CancellationToken ct)
    {
        var request = new CreateAgentRequest
        {
            Name = name, Description = description, ProviderAccountId = providerAccountId,
            ModelId = modelId, Role = role, ContextCapacityTokens = contextCapacityTokens, Capabilities = capabilities
        };
        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(string.Empty, error.ErrorMessage);
            return RedirectToAction(nameof(Index));
        }

        var caps = capabilities.Select(c => Enum.Parse<AgentCapability>(c));
        var agent = Agent.Create(name, description, providerAccountId, modelId, role, contextCapacityTokens, caps);
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(null, "AgentCreated", agent.Id.ToString(), "Success", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        var agent = await _db.Agents.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return NotFound();
        agent.Disable();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(null, "AgentModified", agent.Id.ToString(), "Disabled", Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }
}
