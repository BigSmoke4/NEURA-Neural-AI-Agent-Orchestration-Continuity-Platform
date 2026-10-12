using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Web.Security;

namespace Neura.Web.Controllers;

/// <summary>Knowledge Graph (screen 11): explore project knowledge nodes/edges.</summary>
[Authorize(Policy = "ViewMemory")]
public class KnowledgeController : Controller
{
    private const int MaxGraphNodes = 500;
    private const int MaxGraphEdges = 1_000;

    private readonly NeuraDbContext _db;
    public KnowledgeController(NeuraDbContext db) => _db = db;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var visibleNodes = ResourceScope.KnowledgeNodes(_db.KnowledgeNodes, _db, User);
        var totalNodeCount = await visibleNodes.CountAsync(ct);
        var nodes = await visibleNodes
            .OrderBy(node => node.Name)
            .ThenBy(node => node.Id)
            .Take(MaxGraphNodes)
            .ToListAsync(ct);

        var displayedNodeIds = nodes.Select(node => node.Id).ToArray();
        var visibleEdges = ResourceScope.KnowledgeEdges(_db.KnowledgeEdges, _db, User)
            .Where(edge => displayedNodeIds.Contains(edge.FromNodeId) && displayedNodeIds.Contains(edge.ToNodeId));
        var totalEdgeCount = await visibleEdges.CountAsync(ct);
        var edges = await visibleEdges
            .OrderBy(edge => edge.Id)
            .Take(MaxGraphEdges)
            .ToListAsync(ct);

        // Bound both the DB result and serialized graph payload so a large
        // project cannot produce an unbounded Razor response or canvas graph.
        var elements = nodes.Select(node => (object)new
            { data = new { id = node.Id.ToString(), label = $"{node.Name} ({node.Type})" } })
            .Concat(edges.Select(edge => (object)new
            {
                data = new
                {
                    id = edge.Id.ToString(),
                    source = edge.FromNodeId.ToString(),
                    target = edge.ToNodeId.ToString(),
                    label = edge.Type.ToString()
                }
            }));

        ViewBag.ElementsJson = JsonSerializer.Serialize(elements);
        ViewBag.Edges = edges;
        ViewBag.TotalNodeCount = totalNodeCount;
        ViewBag.TotalEdgeCount = totalEdgeCount;
        ViewBag.GraphTruncated = totalNodeCount > nodes.Count || totalEdgeCount > edges.Count;
        return View(nodes);
    }
}
