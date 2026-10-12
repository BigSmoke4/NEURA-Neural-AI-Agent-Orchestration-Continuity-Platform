using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.KnowledgeGraph.Domain;
using Neura.Modules.Orchestration.Domain;
using Neura.Web.Controllers;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class KnowledgeGraphControllerTests
{
    [Fact]
    public async Task Index_BoundsGraphPayloadAndReportsOmittedRecords()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"knowledge-graph-bounds-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        var ownerId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        db.Missions.Add(Mission.Create(projectId, "Graph mission", "Graph objective", MissionMode.Real, ownerId));
        var nodes = Enumerable.Range(0, 501)
            .Select(index => KnowledgeNode.Create(projectId, KnowledgeNodeType.Module, $"Module {index:D4}"))
            .ToArray();
        var edges = Enumerable.Range(0, 1_001)
            .Select(_ => KnowledgeEdge.Create(nodes[0].Id, nodes[1].Id, KnowledgeEdgeType.RelatedTo))
            .ToArray();
        db.KnowledgeNodes.AddRange(nodes);
        db.KnowledgeEdges.AddRange(edges);
        await db.SaveChangesAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, ownerId.ToString()) }, "test"));
        var controller = new KnowledgeController(db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));
        var displayedNodes = Assert.IsAssignableFrom<IEnumerable<KnowledgeNode>>(result.Model);
        var displayedEdges = Assert.IsAssignableFrom<IEnumerable<KnowledgeEdge>>(result.ViewData["Edges"]);

        Assert.Equal(500, displayedNodes.Count());
        Assert.Equal(1_000, displayedEdges.Count());
        Assert.Equal(501, (int)result.ViewData["TotalNodeCount"]!);
        Assert.Equal(1_001, (int)result.ViewData["TotalEdgeCount"]!);
        Assert.True((bool)result.ViewData["GraphTruncated"]!);
    }
}
