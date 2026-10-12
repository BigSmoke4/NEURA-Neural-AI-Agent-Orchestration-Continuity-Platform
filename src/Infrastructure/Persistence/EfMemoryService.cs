using Microsoft.EntityFrameworkCore;
using Neura.Modules.Memory.Application;
using Neura.Modules.Memory.Domain;

namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Bounded lexical retrieval for project memory. It intentionally avoids
/// claiming semantic/vector search: ranking combines token overlap with the
/// stored importance/confidence values and excludes expired records.
/// </summary>
public sealed class EfMemoryService : IMemoryService
{
    private const int MaxCandidateRecords = 500;

    private readonly NeuraDbContext _db;

    public EfMemoryService(NeuraDbContext db) => _db = db;

    public async Task<IReadOnlyList<MemoryRecord>> RetrieveRelevantAsync(
        Guid projectId,
        string query,
        int maxRecords,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty || maxRecords <= 0)
            return Array.Empty<MemoryRecord>();

        var nowUtc = DateTime.UtcNow;
        var candidates = await _db.Memories.AsNoTracking()
            .Where(memory => memory.ProjectId == projectId &&
                             (!memory.Expiration.HasValue || memory.Expiration.Value > nowUtc))
            .OrderByDescending(memory => memory.Importance * memory.Confidence)
            .ThenByDescending(memory => memory.UpdatedAt)
            .Take(MaxCandidateRecords)
            .ToListAsync(cancellationToken);

        var terms = Tokenize(query);
        var ranked = candidates
            .Select(memory => new
            {
                Memory = memory,
                Score = Score(memory, terms),
                Matched = MatchCount(memory, terms)
            })
            .Where(candidate => terms.Count == 0 || candidate.Matched > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Memory.UpdatedAt)
            .Take(Math.Clamp(maxRecords, 1, 20))
            .Select(candidate => candidate.Memory)
            .ToList();

        return ranked;
    }

    public async Task<int> PurgeExpiredAsync(
        DateTime nowUtc,
        int maxRecords,
        CancellationToken cancellationToken = default)
    {
        if (nowUtc.Kind == DateTimeKind.Local)
            throw new ArgumentException("Expiry cleanup time must be UTC.", nameof(nowUtc));
        if (nowUtc.Kind == DateTimeKind.Unspecified)
            nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        if (maxRecords <= 0)
            return 0;

        var batchSize = Math.Clamp(maxRecords, 1, 5_000);
        var expiredIds = await _db.Memories.AsNoTracking()
            .Where(memory => memory.Expiration.HasValue && memory.Expiration.Value <= nowUtc)
            .OrderBy(memory => memory.Expiration)
            .Select(memory => memory.MemoryId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
        if (expiredIds.Count == 0)
            return 0;

        if (_db.Database.IsRelational())
        {
            return await _db.Memories
                .Where(memory => expiredIds.Contains(memory.MemoryId) &&
                                 memory.Expiration.HasValue && memory.Expiration.Value <= nowUtc)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var expiredRecords = await _db.Memories
            .Where(memory => expiredIds.Contains(memory.MemoryId) &&
                             memory.Expiration.HasValue && memory.Expiration.Value <= nowUtc)
            .ToListAsync(cancellationToken);
        _db.Memories.RemoveRange(expiredRecords);
        await _db.SaveChangesAsync(cancellationToken);
        return expiredRecords.Count;
    }

    private static List<string> Tokenize(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<string>();

        var terms = new List<string>();
        var builder = new System.Text.StringBuilder();
        foreach (var character in query)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                AddToken(builder, terms);
            }
        }
        AddToken(builder, terms);
        return terms.Distinct(StringComparer.Ordinal).Take(32).ToList();
    }

    private static void AddToken(System.Text.StringBuilder builder, ICollection<string> terms)
    {
        if (builder.Length >= 3)
            terms.Add(builder.ToString());
        builder.Clear();
    }

    private static int MatchCount(MemoryRecord memory, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return 0;

        var searchable = memory.Content + " " + string.Join(' ', memory.Tags);
        return terms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static double Score(MemoryRecord memory, IReadOnlyList<string> terms)
    {
        var lexicalScore = terms.Count == 0 ? 0d : MatchCount(memory, terms) / (double)terms.Count;
        var qualityScore = (memory.Importance * 0.65d) + (memory.Confidence * 0.35d);
        return (lexicalScore * 0.75d) + (qualityScore * 0.25d);
    }
}
