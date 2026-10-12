using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Web.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Modules.ProviderIntegration.Domain;
using Neura.Web.Security;
using Neura.Web.Validation;

namespace Neura.Web.Controllers;

/// <summary>Agent Network and Agent Details for provider accounts owned by the caller.</summary>
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
        var agents = await ResourceScope.Agents(_db.Agents, _db, User)
            .OrderBy(a => a.Name)
            .ToListAsync(ct);
        ViewBag.Providers = await ResourceScope.OwnProviders(_db.ProviderAccounts, User)
            .Where(p => p.State == ProviderConnectionState.Connected && p.Kind != ProviderKind.Simulation)
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
        return View(agents);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var agent = await ResourceScope.Agents(_db.Agents, _db, User)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return NotFound();
        return View(agent);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(NeuraRateLimitPolicies.ProviderConfiguration)]
    public async Task<IActionResult> Create(
        string name,
        string? description,
        Guid providerAccountId,
        string modelId,
        string role,
        int contextCapacityTokens,
        string[]? capabilities,
        CancellationToken ct)
    {
        capabilities ??= Array.Empty<string>();
        var request = new CreateAgentRequest
        {
            Name = name,
            Description = description ?? string.Empty,
            ProviderAccountId = providerAccountId,
            ModelId = modelId,
            Role = role,
            ContextCapacityTokens = contextCapacityTokens,
            Capabilities = capabilities
        };

        var validation = await _validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            TempData["AgentError"] = string.Join(" ", validation.Errors.Select(error => error.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }

        var userId = ResourceScope.UserId(User);
        if (!userId.HasValue)
            return Unauthorized();

        // A client cannot attach its agent to another user's provider, even
        // if it guesses a valid account id or submits a forged select value.
        var provider = await _db.ProviderAccounts.FirstOrDefaultAsync(p =>
            p.Id == providerAccountId && p.UserId == userId.Value &&
            p.State == ProviderConnectionState.Connected && p.Kind != ProviderKind.Simulation, ct);
        if (provider is null)
        {
            TempData["AgentError"] = "Choose one of your connected provider accounts.";
            return RedirectToAction(nameof(Index));
        }

        var parsedCapabilities = capabilities
            .Select(value => Enum.Parse<AgentCapability>(value, ignoreCase: true))
            .Distinct()
            .ToArray();
        var agent = Agent.Create(name.Trim(), description?.Trim() ?? string.Empty,
            provider.Id, modelId.Trim(), role.Trim(), contextCapacityTokens, parsedCapabilities);
        _db.Agents.Add(agent);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(userId, "AgentCreated", agent.Id.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct)
    {
        var agent = await ResourceScope.Agents(_db.Agents, _db, User)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return NotFound();

        agent.Disable();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ResourceScope.UserId(User), "AgentModified", agent.Id.ToString(), "Disabled",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct)
    {
        var agent = await ResourceScope.Agents(_db.Agents, _db, User)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agent is null) return NotFound();

        agent.Enable();
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ResourceScope.UserId(User), "AgentModified", agent.Id.ToString(), "Enabled",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }
}
