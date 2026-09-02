using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.ContextManagement.Domain;

namespace Neura.Web.Controllers;

/// <summary>
/// Context Explorer (screen 9): inspect real, persisted context handoff
/// packages built by the Context Continuity Engine. Index renders an
/// overview graph (Mission → Task → Package, one node per package) using
/// the same Cytoscape.js approach as Knowledge Graph; Details renders a
/// per-package graph (Package → category → individual item) alongside
/// the existing full field-level table.
/// </summary>
[Authorize(Policy = "ViewMemory")]
public class ContextController : Controller
{
    private readonly NeuraDbContext _db;
    public ContextController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var packages = await _db.ContextPackages.OrderByDescending(p => p.CreatedAtUtc).Take(50).ToListAsync(ct);

        var elements = new List<object>();
        foreach (var p in packages)
        {
            var missionNodeId = $"mission-{p.MissionId}";
            var taskNodeId = $"task-{p.TaskId}";
            var packageNodeId = $"package-{p.Id}";

            elements.Add(new { data = new { id = missionNodeId, label = p.Mission.Length > 24 ? p.Mission[..24] + "…" : p.Mission } });
            elements.Add(new { data = new { id = taskNodeId, label = p.CurrentTask.Length > 24 ? p.CurrentTask[..24] + "…" : p.CurrentTask } });
            elements.Add(new { data = new { id = packageNodeId, label = $"Package ({p.Status})", packageId = p.Id.ToString() } });
            elements.Add(new { data = new { id = $"e-{missionNodeId}-{taskNodeId}", source = missionNodeId, target = taskNodeId, label = "has task" } });
            elements.Add(new { data = new { id = $"e-{taskNodeId}-{packageNodeId}", source = taskNodeId, target = packageNodeId, label = "handed off" } });
        }

        ViewBag.ElementsJson = JsonSerializer.Serialize(elements);
        return View(packages);
    }

    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var package = await _db.ContextPackages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (package is null) return NotFound();

        ViewBag.ElementsJson = JsonSerializer.Serialize(BuildPackageGraph(package));
        return View(package);
    }

    /// <summary>
    /// Package → category (Completed Work / Remaining Work / Decisions /
    /// Constraints / Files Changed / Errors / Tests / Dependencies / Open
    /// Questions / Relevant Memory) → individual item, capped per
    /// category so the graph stays legible per section 56's guidance on
    /// not rendering unbounded node counts.
    /// </summary>
    private static List<object> BuildPackageGraph(ContextHandoffPackage p)
    {
        var elements = new List<object>();
        var rootId = $"package-{p.Id}";
        elements.Add(new { data = new { id = rootId, label = "Package" } });

        void AddCategory(string label, IReadOnlyList<string> items)
        {
            if (items.Count == 0) return;
            var categoryId = $"{rootId}-{label}";
            elements.Add(new { data = new { id = categoryId, label } });
            elements.Add(new { data = new { id = $"e-{rootId}-{categoryId}", source = rootId, target = categoryId } });

            foreach (var (item, index) in items.Take(8).Select((v, i) => (v, i)))
            {
                var itemId = $"{categoryId}-{index}";
                var short_ = item.Length > 40 ? item[..40] + "…" : item;
                elements.Add(new { data = new { id = itemId, label = short_ } });
                elements.Add(new { data = new { id = $"e-{categoryId}-{itemId}", source = categoryId, target = itemId } });
            }
        }

        AddCategory("Completed Work", p.CompletedWork);
        AddCategory("Remaining Work", p.RemainingWork);
        AddCategory("Decisions", p.Decisions);
        AddCategory("Constraints", p.Constraints);
        AddCategory("Files Changed", p.FilesChanged);
        AddCategory("Errors", p.Errors);
        AddCategory("Tests", p.Tests);
        AddCategory("Dependencies", p.Dependencies);
        AddCategory("Open Questions", p.OpenQuestions);
        AddCategory("Relevant Memory", p.RelevantMemory);

        return elements;
    }
}
