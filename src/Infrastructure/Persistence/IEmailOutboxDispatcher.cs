namespace Neura.Infrastructure.Persistence;

/// <summary>Claims and attempts at most one durable email delivery.</summary>
public interface IEmailOutboxDispatcher
{
    /// <returns>True if a message was claimed, whether delivery succeeded or was rescheduled.</returns>
    Task<bool> DispatchNextAsync(CancellationToken cancellationToken);
}
