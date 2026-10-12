using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.KnowledgeGraph.Domain;
using Neura.Modules.Memory.Domain;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Domain;
using Neura.Web.Controllers;
using Neura.Web.Security;
using Xunit;

namespace Neura.Tests.Security;

public sealed class ResourceScopeTests
{
    [Fact]
    public async Task OrdinaryUser_SeesOnlyOwnedMissionsAndProviders()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"resource-scope-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var ownedMission = Mission.Create(Guid.NewGuid(), "owned", "objective", MissionMode.Real, ownerId);
        var otherMission = Mission.Create(Guid.NewGuid(), "other", "objective", MissionMode.Real, otherId);
        var ownedProvider = AIProviderAccount.Connect(ownerId, ProviderKind.OpenAI, "owner", "protected-owner");
        var otherProvider = AIProviderAccount.Connect(otherId, ProviderKind.Anthropic, "other", "protected-other");
        db.Missions.AddRange(ownedMission, otherMission);
        db.ProviderAccounts.AddRange(ownedProvider, otherProvider);
        await db.SaveChangesAsync();

        var principal = Principal(ownerId);
        var visibleMissionIds = await ResourceScope.Missions(db.Missions, principal)
            .Select(mission => mission.Id)
            .ToListAsync();
        var visibleProviderIds = await ResourceScope.OwnProviders(db.ProviderAccounts, principal)
            .Select(provider => provider.Id)
            .ToListAsync();

        Assert.Equal(new[] { ownedMission.Id }, visibleMissionIds);
        Assert.Equal(new[] { ownedProvider.Id }, visibleProviderIds);
    }

    [Fact]
    public async Task OrdinaryUser_SeesOnlyMemoryFromOwnedProjectScopes()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"memory-resource-scope-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        var ownerId = Guid.NewGuid();
        var ownedProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        db.Missions.Add(Mission.Create(ownedProjectId, "owned", "objective", MissionMode.Real, ownerId));
        var ownedMemory = MemoryRecord.Create(ownedProjectId, MemoryType.Semantic,
            "Owned project fact.", 0.8, 0.9, null, null);
        var otherMemory = MemoryRecord.Create(otherProjectId, MemoryType.Semantic,
            "Another project's fact.", 0.8, 0.9, null, null);
        db.Memories.AddRange(ownedMemory, otherMemory);
        await db.SaveChangesAsync();

        var visibleMemoryIds = await ResourceScope.Memories(db.Memories, db, Principal(ownerId))
            .Select(memory => memory.MemoryId)
            .ToListAsync();

        Assert.Equal(new[] { ownedMemory.MemoryId }, visibleMemoryIds);
    }

    [Fact]
    public async Task OperationalRole_CanSeeAllMissionsButProviderCredentialsRemainOwnerScoped()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"resource-scope-roles-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        var adminId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var firstMission = Mission.Create(Guid.NewGuid(), "first", "objective", MissionMode.Real, adminId);
        var secondMission = Mission.Create(Guid.NewGuid(), "second", "objective", MissionMode.Real, otherId);
        var adminProvider = AIProviderAccount.Connect(adminId, ProviderKind.OpenAI, "admin", "protected-admin");
        _ = AIProviderAccount.Connect(otherId, ProviderKind.Anthropic, "other", "protected-other");
        db.Missions.AddRange(firstMission, secondMission);
        db.ProviderAccounts.AddRange(adminProvider);
        await db.SaveChangesAsync();

        var admin = Principal(adminId, "Admin");
        var visibleMissionCount = await ResourceScope.Missions(db.Missions, admin).CountAsync();
        var visibleProviderIds = await ResourceScope.OwnProviders(db.ProviderAccounts, admin)
            .Select(provider => provider.Id)
            .ToListAsync();

        Assert.Equal(2, visibleMissionCount);
        Assert.Equal(new[] { adminProvider.Id }, visibleProviderIds);
    }

    [Fact]
    public async Task OrdinaryUser_CannotSeeKnowledgeEdgesTouchingAnotherProjectsNode()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"resource-scope-graph-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);

        var ownerId = Guid.NewGuid();
        var ownerProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var mission = Mission.Create(ownerProjectId, "owned", "objective", MissionMode.Real, ownerId);
        var ownNode = KnowledgeNode.Create(ownerProjectId, KnowledgeNodeType.Module, "own node");
        var otherNode = KnowledgeNode.Create(otherProjectId, KnowledgeNodeType.Module, "other node");
        var ownEdge = KnowledgeEdge.Create(ownNode.Id, ownNode.Id, KnowledgeEdgeType.RelatedTo);
        var crossProjectEdge = KnowledgeEdge.Create(ownNode.Id, otherNode.Id, KnowledgeEdgeType.RelatedTo);
        db.Missions.Add(mission);
        db.KnowledgeNodes.AddRange(ownNode, otherNode);
        db.KnowledgeEdges.AddRange(ownEdge, crossProjectEdge);
        await db.SaveChangesAsync();

        var principal = Principal(ownerId);
        var visibleNodeIds = await ResourceScope.KnowledgeNodes(db.KnowledgeNodes, db, principal)
            .Select(node => node.Id)
            .ToListAsync();
        var visibleEdgeIds = await ResourceScope.KnowledgeEdges(db.KnowledgeEdges, db, principal)
            .Select(edge => edge.Id)
            .ToListAsync();

        Assert.Equal(new[] { ownNode.Id }, visibleNodeIds);
        Assert.Equal(new[] { ownEdge.Id }, visibleEdgeIds);
    }

    [Fact]
    public void AuditorSensitiveActions_RequireDedicatedMutationPolicies()
    {
        AssertActionPolicy<BrainController>(nameof(BrainController.RunSimulationDemo), "ExecuteMission");
        AssertActionPolicy<MemoryController>(nameof(MemoryController.Delete), "ManageMemory");
        var memoryEditActions = typeof(MemoryController).GetMethods()
            .Where(action => action.Name == nameof(MemoryController.Edit))
            .ToArray();
        Assert.Equal(2, memoryEditActions.Length);
        Assert.All(memoryEditActions, action =>
            Assert.Equal("ManageMemory", action.GetCustomAttribute<AuthorizeAttribute>()?.Policy));
        AssertActionPolicy<NotificationsController>(nameof(NotificationsController.MarkRead), "ManageNotifications");
    }

    private static void AssertActionPolicy<TController>(string actionName, string expectedPolicy)
    {
        var action = typeof(TController).GetMethod(actionName);
        Assert.NotNull(action);
        var authorize = action!.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(expectedPolicy, authorize!.Policy);
    }

    private static ClaimsPrincipal Principal(Guid userId, string? role = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (role is not null)
            claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }
}
