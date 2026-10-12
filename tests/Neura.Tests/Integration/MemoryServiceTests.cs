using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Memory.Domain;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class MemoryServiceTests
{
    [Fact]
    public async Task Retrieval_IsProjectScopedRelevantAndExcludesExpiredRecords()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"memory-retrieval-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var relevant = MemoryRecord.Create(projectId, MemoryType.LongTermProject,
            "Use PostgreSQL constraints for checkout order data.", 0.9, 0.95, null, null,
            new[] { "database", "checkout" });
        var unrelated = MemoryRecord.Create(projectId, MemoryType.Semantic,
            "The mobile application uses a purple theme.", 0.8, 0.9, null, null);
        var expired = MemoryRecord.Create(projectId, MemoryType.Episodic,
            "PostgreSQL migration was reverted.", 1, 1, null, DateTime.UtcNow.AddDays(-1));
        var crossProject = MemoryRecord.Create(otherProjectId, MemoryType.Semantic,
            "PostgreSQL checkout uses a different schema.", 1, 1, null, null);
        db.Memories.AddRange(relevant, unrelated, expired, crossProject);
        await db.SaveChangesAsync();

        var service = new EfMemoryService(db);
        var results = await service.RetrieveRelevantAsync(projectId, "PostgreSQL checkout schema", 5);

        Assert.Single(results);
        Assert.Equal(relevant.MemoryId, results[0].MemoryId);
        Assert.DoesNotContain(results, memory => memory.MemoryId == expired.MemoryId);
        Assert.DoesNotContain(results, memory => memory.MemoryId == crossProject.MemoryId);
    }

    [Fact]
    public async Task ExpiryCleanup_DeletesOnlyExpiredRecordsInBoundedBatch()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"memory-expiry-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var expiredOne = MemoryRecord.Create(projectId, MemoryType.ShortTerm, "expired one", 0.2, 0.5,
            null, now.AddMinutes(-2));
        var expiredTwo = MemoryRecord.Create(projectId, MemoryType.Working, "expired two", 0.3, 0.6,
            null, now.AddMinutes(-1));
        var active = MemoryRecord.Create(projectId, MemoryType.LongTermProject, "keep me", 0.8, 0.9,
            null, now.AddDays(1));
        db.Memories.AddRange(expiredOne, expiredTwo, active);
        await db.SaveChangesAsync();

        var service = new EfMemoryService(db);
        var deleted = await service.PurgeExpiredAsync(now, maxRecords: 1);

        Assert.Equal(1, deleted);
        Assert.Equal(2, await db.Memories.CountAsync());
        Assert.True(await db.Memories.AnyAsync(memory => memory.MemoryId == active.MemoryId));
    }
}
