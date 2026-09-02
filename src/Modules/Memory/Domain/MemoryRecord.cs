namespace Neura.Modules.Memory.Domain;

public enum MemoryType { ShortTerm, Working, LongTermProject, Episodic, Semantic }

public class MemoryRecord
{
    public Guid MemoryId { get; private set; } = Guid.NewGuid();
    public Guid ProjectId { get; private set; }
    public MemoryType Type { get; private set; }
    public string Content { get; private set; } = default!;
    public double Importance { get; private set; }
    public double Confidence { get; private set; }
    public Guid SourceAgentId { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? Expiration { get; private set; }
    public List<string> Tags { get; private set; } = new();

    private MemoryRecord() { }

    public static MemoryRecord Create(Guid projectId, MemoryType type, string content, double importance,
        double confidence, Guid sourceAgentId, DateTime? expiration, IEnumerable<string>? tags = null)
        => new()
        {
            ProjectId = projectId,
            Type = type,
            Content = content,
            Importance = importance,
            Confidence = confidence,
            SourceAgentId = sourceAgentId,
            Expiration = expiration,
            Tags = tags?.ToList() ?? new()
        };

    public bool IsExpired(DateTime nowUtc) => Expiration.HasValue && Expiration.Value <= nowUtc;
}
