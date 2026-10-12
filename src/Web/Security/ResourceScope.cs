using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.AgentManagement.Domain;
using Neura.Modules.ContextManagement.Domain;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.KnowledgeGraph.Domain;
using Neura.Modules.Memory.Domain;
using Neura.Modules.Observability.Domain;
using Neura.Modules.Orchestration.Domain;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Web.Security;

/// <summary>
/// Query-level resource scoping for the current role model. Ordinary users
/// only see resources linked to their own missions/projects/provider accounts;
/// Admin, Operator, and Auditor are explicit cross-user operational roles.
/// Provider credentials are always owner-scoped, including for Admins.
/// </summary>
public static class ResourceScope
{
    public static Guid? UserId(ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static bool CanViewAllOperationalData(ClaimsPrincipal principal)
        => principal.IsInRole("Admin") || principal.IsInRole("Operator") || principal.IsInRole("Auditor");

    public static IQueryable<Mission> Missions(
        IQueryable<Mission> query,
        ClaimsPrincipal principal)
    {
        if (CanViewAllOperationalData(principal))
            return query;

        var userId = UserId(principal);
        return userId.HasValue ? query.Where(m => m.OwnerUserId == userId.Value) : query.Where(_ => false);
    }

    public static IQueryable<Guid> MissionIds(
        IQueryable<Mission> query,
        ClaimsPrincipal principal)
        => Missions(query, principal).Select(m => m.Id);

    public static IQueryable<Guid> ProjectIds(
        IQueryable<Mission> query,
        ClaimsPrincipal principal)
        => Missions(query, principal).Select(m => m.ProjectId).Distinct();

    public static IQueryable<AIProviderAccount> OwnProviders(
        IQueryable<AIProviderAccount> query,
        ClaimsPrincipal principal)
    {
        var userId = UserId(principal);
        return userId.HasValue ? query.Where(p => p.UserId == userId.Value) : query.Where(_ => false);
    }

    public static IQueryable<Agent> Agents(
        IQueryable<Agent> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
    {
        if (CanViewAllOperationalData(principal))
            return query;

        var userId = UserId(principal);
        if (!userId.HasValue)
            return query.Where(_ => false);

        return query.Where(agent => db.ProviderAccounts.Any(provider =>
            provider.Id == agent.ProviderAccountId && provider.UserId == userId.Value));
    }

    public static IQueryable<MemoryRecord> Memories(
        IQueryable<MemoryRecord> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(memory => ProjectIds(db.Missions, principal).Contains(memory.ProjectId));

    public static IQueryable<KnowledgeNode> KnowledgeNodes(
        IQueryable<KnowledgeNode> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(node => ProjectIds(db.Missions, principal).Contains(node.ProjectId));

    public static IQueryable<KnowledgeEdge> KnowledgeEdges(
        IQueryable<KnowledgeEdge> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
    {
        if (CanViewAllOperationalData(principal))
            return query;

        var visibleNodeIds = KnowledgeNodes(db.KnowledgeNodes, db, principal).Select(node => node.Id);
        return query.Where(edge => visibleNodeIds.Contains(edge.FromNodeId) && visibleNodeIds.Contains(edge.ToNodeId));
    }

    public static IQueryable<HandoffRecord> Handoffs(
        IQueryable<HandoffRecord> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(handoff => MissionIds(db.Missions, principal).Contains(handoff.MissionId));

    public static IQueryable<ContextHandoffPackage> ContextPackages(
        IQueryable<ContextHandoffPackage> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(package => MissionIds(db.Missions, principal).Contains(package.MissionId));

    public static IQueryable<ExecutionEvent> ExecutionEvents(
        IQueryable<ExecutionEvent> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(evt => MissionIds(db.Missions, principal).Contains(evt.MissionId));

    public static IQueryable<CostRecord> CostRecords(
        IQueryable<CostRecord> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(cost => cost.MissionId.HasValue &&
                MissionIds(db.Missions, principal).Contains(cost.MissionId.Value));

    public static IQueryable<Notification> Notifications(
        IQueryable<Notification> query,
        NeuraDbContext db,
        ClaimsPrincipal principal)
        => CanViewAllOperationalData(principal)
            ? query
            : query.Where(notification => notification.MissionId.HasValue &&
                MissionIds(db.Missions, principal).Contains(notification.MissionId.Value));
}
