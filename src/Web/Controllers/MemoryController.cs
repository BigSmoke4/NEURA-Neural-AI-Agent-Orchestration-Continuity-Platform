using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Neura.Web.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Memory.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Web.Security;
using Neura.Web.ViewModels;

namespace Neura.Web.Controllers;

/// <summary>Memory Center: retrieve, curate, and safely delete project memory.</summary>
[EnableRateLimiting(NeuraRateLimitPolicies.MissionCreation)]
[Authorize(Policy = "ViewMemory")]
public class MemoryController : Controller
{
    private readonly NeuraDbContext _db;
    private readonly IAuditLogger _audit;

    public MemoryController(NeuraDbContext db, IAuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [DisableRateLimiting]
    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        var term = q?.Trim();
        if (term?.Length > 120)
            term = term[..120];

        return View(await BuildCenterAsync(term, ct));
    }

    [HttpGet]
    [DisableRateLimiting]
    [Authorize(Policy = "ManageMemory")]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var record = await ResourceScope.Memories(_db.Memories, _db, User)
            .AsNoTracking()
            .FirstOrDefaultAsync(memory => memory.MemoryId == id, ct);
        if (record is null) return NotFound();

        return View(new MemoryEditViewModel
        {
            MemoryId = record.MemoryId,
            ProjectId = record.ProjectId,
            Type = record.Type.ToString(),
            Content = record.Content,
            Importance = record.Importance,
            Confidence = record.Confidence,
            Tags = string.Join(", ", record.Tags),
            KeepExpiration = true,
            CurrentExpirationUtc = record.Expiration
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "ManageMemory")]
    public async Task<IActionResult> Edit(MemoryEditViewModel model, CancellationToken ct)
    {
        var record = await ResourceScope.Memories(_db.Memories, _db, User)
            .FirstOrDefaultAsync(memory => memory.MemoryId == model.MemoryId, ct);
        if (record is null) return NotFound();

        if (!Enum.TryParse<MemoryType>(model.Type, ignoreCase: true, out var memoryType) || !Enum.IsDefined(memoryType))
            ModelState.AddModelError(nameof(model.Type), "Choose a valid memory type.");
        if (string.IsNullOrWhiteSpace(model.Content) || model.Content.Trim().Length > 4_000)
            ModelState.AddModelError(nameof(model.Content), "Memory content is required and must be at most 4,000 characters.");
        if (!double.IsFinite(model.Importance) || model.Importance is < 0 or > 1)
            ModelState.AddModelError(nameof(model.Importance), "Importance must be between zero and one.");
        if (!double.IsFinite(model.Confidence) || model.Confidence is < 0 or > 1)
            ModelState.AddModelError(nameof(model.Confidence), "Confidence must be between zero and one.");
        if (!model.KeepExpiration && model.RetentionDays is < 1 or > 3_650)
            ModelState.AddModelError(nameof(model.RetentionDays), "Retention must be blank or between 1 and 3,650 days.");

        var normalizedTags = ParseTags(model.Tags);
        if (normalizedTags is null)
            ModelState.AddModelError(nameof(model.Tags), "Use at most ten tags, each no longer than 50 characters.");

        model.ProjectId = record.ProjectId;
        model.CurrentExpirationUtc = record.Expiration;
        if (!ModelState.IsValid)
            return View(model);

        DateTime? expiration = model.KeepExpiration
            ? record.Expiration
            : model.RetentionDays.HasValue ? DateTime.UtcNow.AddDays(model.RetentionDays.Value) : null;
        record.Revise(memoryType, model.Content, model.Importance, model.Confidence, expiration, normalizedTags);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ResourceScope.UserId(User), "MemoryUpdated", record.MemoryId.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        TempData["MemoryMessage"] = "Project memory updated. Retrieved memory remains untrusted reference context in provider prompts.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "ManageMemory")]
    public async Task<IActionResult> Create(
        Guid projectId,
        string type,
        string content,
        double importance,
        double confidence,
        string? tags,
        int? retentionDays,
        CancellationToken ct)
    {
        if (!await ResourceScope.ProjectIds(_db.Missions, User).AnyAsync(id => id == projectId, ct))
            return NotFound();

        if (!Enum.TryParse<MemoryType>(type, ignoreCase: true, out var memoryType) || !Enum.IsDefined(memoryType))
            ModelState.AddModelError(nameof(type), "Choose a valid memory type.");
        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 4_000)
            ModelState.AddModelError(nameof(content), "Memory content is required and must be at most 4,000 characters.");
        if (!double.IsFinite(importance) || importance is < 0 or > 1)
            ModelState.AddModelError(nameof(importance), "Importance must be between zero and one.");
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
            ModelState.AddModelError(nameof(confidence), "Confidence must be between zero and one.");
        if (retentionDays is < 1 or > 3_650)
            ModelState.AddModelError(nameof(retentionDays), "Retention must be blank or between 1 and 3,650 days.");

        var normalizedTags = ParseTags(tags);
        if (normalizedTags is null)
            ModelState.AddModelError(nameof(tags), "Use at most ten tags, each no longer than 50 characters.");

        if (!ModelState.IsValid)
            return View(nameof(Index), await BuildCenterAsync(null, ct));

        DateTime? expiration = retentionDays.HasValue ? DateTime.UtcNow.AddDays(retentionDays.Value) : null;
        var record = MemoryRecord.Create(projectId, memoryType, content, importance, confidence,
            sourceAgentId: null, expiration: expiration, tags: normalizedTags);

        _db.Memories.Add(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ResourceScope.UserId(User), "MemoryCreated", record.MemoryId.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        TempData["MemoryMessage"] = "Project memory saved. Retrieved memory is treated as untrusted context in provider prompts.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = "ManageMemory")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var record = await ResourceScope.Memories(_db.Memories, _db, User)
            .FirstOrDefaultAsync(m => m.MemoryId == id, ct);
        if (record is null) return NotFound();

        _db.Memories.Remove(record);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(ResourceScope.UserId(User), "MemoryDeleted", record.MemoryId.ToString(), "Success",
            Guid.NewGuid(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return RedirectToAction(nameof(Index));
    }

    private async Task<MemoryCenterViewModel> BuildCenterAsync(string? query, CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var memoryQuery = ResourceScope.Memories(_db.Memories, _db, User)
            .Where(memory => !memory.Expiration.HasValue || memory.Expiration.Value > nowUtc);
        if (!string.IsNullOrWhiteSpace(query))
            memoryQuery = memoryQuery.Where(memory => memory.Content.Contains(query));

        var memories = await memoryQuery
            .OrderByDescending(memory => memory.CreatedAt)
            .Take(100)
            .ToListAsync(ct);

        var projectRows = await ResourceScope.Missions(_db.Missions, User)
            .OrderByDescending(mission => mission.CreatedAtUtc)
            .Select(mission => new { mission.ProjectId, mission.Title })
            .Take(1_000)
            .ToListAsync(ct);
        var projects = projectRows
            .GroupBy(row => row.ProjectId)
            .Select(group => new MemoryProjectChoice(group.Key, group.First().Title))
            .OrderBy(project => project.Label)
            .Take(100)
            .ToList();

        return new MemoryCenterViewModel(memories, projects, query);
    }

    private static List<string>? ParseTags(string? rawTags)
    {
        if (string.IsNullOrWhiteSpace(rawTags))
            return new List<string>();
        if (rawTags.Length > 512)
            return null;

        var tags = rawTags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tags.Count <= 10 && tags.All(tag => tag.Length <= 50) ? tags : null;
    }
}
