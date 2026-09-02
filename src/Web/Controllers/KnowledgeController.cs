using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;

namespace Neura.Web.Controllers;

/// <summary>Knowledge Graph (screen 11): explore project knowledge nodes/edges.</summary>
[Authorize(Policy = "ViewMemory")]
public class KnowledgeController : Controller
{
    private readonly NeuraDbContext _db;
    public KnowledgeController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var nodes = await _db.KnowledgeNodes.ToListAsync(ct);
        var edges = await _db.KnowledgeEdges.ToListAsync(ct);

        // Cytoscape element JSON for the interactive graph, built server-side
        // from real KnowledgeNodes/KnowledgeEdges rows — the same rendering
        // approach as the Brain dashboard's neural graph, applied here too.
        var elements = nodes.Select(n => new { data = new { id = n.Id.ToString(), label = $"{n.Name} ({n.Type})" } })
            .Concat(edges.Select(e => (object)new { data = new { id = e.Id.ToString(), source = e.FromNodeId.ToString(), target = e.ToNodeId.ToString(), label = e.Type.ToString() } }));

        ViewBag.ElementsJson = JsonSerializer.Serialize(elements);
        ViewBag.Edges = edges;
        return View(nodes);
    }
}
