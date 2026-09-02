namespace Neura.Modules.KnowledgeGraph.Domain;

public enum KnowledgeNodeType
{
    Project, Module, Class, Interface, DatabaseTable, Api, Service,
    Controller, View, Agent, Task, Decision, Requirement, Error, Test, Dependency, Memory
}

public enum KnowledgeEdgeType
{
    DependsOn, Implements, Calls, Contains, BelongsTo, CreatedBy,
    ModifiedBy, TestedBy, Caused, ResolvedBy, RelatedTo, ReplacedBy
}

public class KnowledgeNode
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ProjectId { get; private set; }
    public KnowledgeNodeType Type { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }

    private KnowledgeNode() { }
    public static KnowledgeNode Create(Guid projectId, KnowledgeNodeType type, string name, string? description = null)
        => new() { ProjectId = projectId, Type = type, Name = name, Description = description };
}

public class KnowledgeEdge
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid FromNodeId { get; private set; }
    public Guid ToNodeId { get; private set; }
    public KnowledgeEdgeType Type { get; private set; }

    private KnowledgeEdge() { }
    public static KnowledgeEdge Create(Guid fromNodeId, Guid toNodeId, KnowledgeEdgeType type)
        => new() { FromNodeId = fromNodeId, ToNodeId = toNodeId, Type = type };
}
