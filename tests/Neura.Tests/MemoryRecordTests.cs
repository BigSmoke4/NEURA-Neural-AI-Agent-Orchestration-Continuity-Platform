using Neura.Modules.Memory.Domain;
using Xunit;

namespace Neura.Tests;

public sealed class MemoryRecordTests
{
    [Fact]
    public void Revise_UpdatesContentAndQualityWithoutChangingIdentityOrProvenance()
    {
        var projectId = Guid.NewGuid();
        var sourceAgentId = Guid.NewGuid();
        var originalExpiry = DateTime.UtcNow.AddDays(10);
        var record = MemoryRecord.Create(projectId, MemoryType.LongTermProject,
            "Original checkout constraint.", 0.6, 0.7, sourceAgentId, originalExpiry,
            new[] { "checkout" });
        var memoryId = record.MemoryId;
        var createdAt = record.CreatedAt;
        var updatedAt = record.UpdatedAt;
        var nextExpiry = DateTime.UtcNow.AddDays(30);

        record.Revise(MemoryType.Semantic, "Revised checkout invariant.", 0.9, 0.95,
            nextExpiry, new[] { "checkout", "idempotency" });

        Assert.Equal(memoryId, record.MemoryId);
        Assert.Equal(projectId, record.ProjectId);
        Assert.Equal(sourceAgentId, record.SourceAgentId.GetValueOrDefault());
        Assert.Equal(createdAt, record.CreatedAt);
        Assert.True(record.UpdatedAt >= updatedAt);
        Assert.Equal(MemoryType.Semantic, record.Type);
        Assert.Equal("Revised checkout invariant.", record.Content);
        Assert.Equal(0.9, record.Importance);
        Assert.Equal(0.95, record.Confidence);
        Assert.Equal(nextExpiry, record.Expiration.GetValueOrDefault());
        Assert.Equal(new[] { "checkout", "idempotency" }, record.Tags);
    }

    [Fact]
    public void Revise_RejectsInvalidInputWithoutPartiallyChangingRecord()
    {
        var record = MemoryRecord.Create(Guid.NewGuid(), MemoryType.Working,
            "Keep existing memory.", 0.5, 0.6, null, null, new[] { "stable" });
        var memoryId = record.MemoryId;
        var updatedAt = record.UpdatedAt;

        Assert.Throws<ArgumentException>(() => record.Revise(MemoryType.Semantic,
            "  ", 0.9, 0.9, null, new[] { "changed" }));

        Assert.Equal(memoryId, record.MemoryId);
        Assert.Equal(MemoryType.Working, record.Type);
        Assert.Equal("Keep existing memory.", record.Content);
        Assert.Equal(0.5, record.Importance);
        Assert.Equal(0.6, record.Confidence);
        Assert.Equal(new[] { "stable" }, record.Tags);
        Assert.Equal(updatedAt, record.UpdatedAt);
    }
}
