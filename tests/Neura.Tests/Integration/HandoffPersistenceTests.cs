using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.Orchestration.Application;
using Xunit;

namespace Neura.Tests.Integration;

public sealed class HandoffPersistenceTests
{
    [Fact]
    public async Task HandoffSink_PersistsLifecycleTransitionsForReplayAndInspection()
    {
        var options = new DbContextOptionsBuilder<NeuraDbContext>()
            .UseInMemoryDatabase($"handoff-persistence-{Guid.NewGuid():N}")
            .Options;
        await using var db = new NeuraDbContext(options);
        var sink = new EfHandoffSink(db);
        var handoff = HandoffRecord.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            HandoffReason.ContextExhaustion, Guid.NewGuid());

        handoff.BeginTransfer();
        await sink.SaveAsync(handoff);
        handoff.AssignReceiver(Guid.NewGuid());
        await sink.SaveAsync(handoff);
        handoff.Validate(true, "Required state present.");
        await sink.SaveAsync(handoff);

        db.ChangeTracker.Clear();
        var persisted = await db.Handoffs.SingleAsync(x => x.Id == handoff.Id);
        Assert.Equal(HandoffState.Completed, persisted.State);
        Assert.Equal(HandoffReason.ContextExhaustion, persisted.Reason);
        Assert.Equal(handoff.ToAgentId, persisted.ToAgentId);
        Assert.NotNull(persisted.CompletedAtUtc);
    }
}
