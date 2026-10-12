namespace Neura.Modules.Memory.Domain;

public enum MemoryType { ShortTerm, Working, LongTermProject, Episodic, Semantic }

/// <summary>
/// Project-scoped memory with explicit provenance, lifecycle, and bounded
/// quality metadata. User-entered memories have no source agent; agent-origin
/// values retain the source agent identifier. All memory remains untrusted
/// context when supplied to a model.
/// </summary>
public class MemoryRecord
{
    public Guid MemoryId { get; private set; } = Guid.NewGuid();
    public Guid ProjectId { get; private set; }
    public MemoryType Type { get; private set; }
    public string Content { get; private set; } = default!;
    public double Importance { get; private set; }
    public double Confidence { get; private set; }
    public Guid? SourceAgentId { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? Expiration { get; private set; }
    public List<string> Tags { get; private set; } = new();

    private MemoryRecord() { }

    public static MemoryRecord Create(Guid projectId, MemoryType type, string content, double importance,
        double confidence, Guid? sourceAgentId, DateTime? expiration, IEnumerable<string>? tags = null)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("A memory must belong to a project.", nameof(projectId));
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));
        if (string.IsNullOrWhiteSpace(content) || content.Length > 4_000)
            throw new ArgumentException("Memory content must contain 1 to 4,000 characters.", nameof(content));
        if (!double.IsFinite(importance) || importance is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(importance), "Importance must be between zero and one.");
        if (!double.IsFinite(confidence) || confidence is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between zero and one.");
        if (sourceAgentId == Guid.Empty)
            throw new ArgumentException("Source agent id must be null or a valid id.", nameof(sourceAgentId));
        if (expiration.HasValue && expiration.Value.Kind == DateTimeKind.Local)
            throw new ArgumentException("Expiration must be represented in UTC.", nameof(expiration));

        var normalizedTags = (tags ?? Array.Empty<string>())
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedTags.Count > 10 || normalizedTags.Any(tag => tag.Length > 50))
            throw new ArgumentException("A memory can have at most ten tags, each up to 50 characters.", nameof(tags));

        return new MemoryRecord
        {
            ProjectId = projectId,
            Type = type,
            Content = content.Trim(),
            Importance = importance,
            Confidence = confidence,
            SourceAgentId = sourceAgentId,
            Expiration = expiration.HasValue && expiration.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(expiration.Value, DateTimeKind.Utc)
                : expiration,
            Tags = normalizedTags
        };
    }

    /// <summary>
    /// Revises mutable memory data without changing its identity, project, or
    /// provenance. Validation is completed before any field is changed.
    /// </summary>
    public void Revise(MemoryType type, string content, double importance, double confidence,
        DateTime? expiration, IEnumerable<string>? tags = null)
    {
        var revised = Create(ProjectId, type, content, importance, confidence,
            SourceAgentId, expiration, tags);

        Type = revised.Type;
        Content = revised.Content;
        Importance = revised.Importance;
        Confidence = revised.Confidence;
        Expiration = revised.Expiration;
        Tags = revised.Tags;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsExpired(DateTime nowUtc) => Expiration.HasValue && Expiration.Value <= nowUtc;
}
