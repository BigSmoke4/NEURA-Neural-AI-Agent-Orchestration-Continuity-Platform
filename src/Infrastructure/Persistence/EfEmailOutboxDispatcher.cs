using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Neura.Infrastructure.Email;
using Neura.Modules.Orchestration.Application;

namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Acquires rows with a compare-and-set lease so separate web instances can
/// drain one shared PostgreSQL outbox without normally sending the same row at
/// once. SMTP delivery is at-least-once; a crash between SMTP acceptance and
/// the sent marker can cause a later retry to deliver a duplicate.
/// </summary>
public sealed class EfEmailOutboxDispatcher : IEmailOutboxDispatcher
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEmailTransport _transport;
    private readonly IDataProtector _protector;
    private readonly ILogger<EfEmailOutboxDispatcher> _logger;
    private readonly TimeSpan _leaseDuration;
    private readonly int _maxAttempts;
    private readonly int _initialRetryDelaySeconds;
    private readonly int _maximumRetryDelaySeconds;

    public EfEmailOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IEmailTransport transport,
        IDataProtectionProvider protectionProvider,
        IOptions<EmailOutboxOptions> options,
        ILogger<EfEmailOutboxDispatcher> logger)
    {
        var configured = options.Value;
        _scopeFactory = scopeFactory;
        _transport = transport;
        _protector = protectionProvider.CreateProtector("Neura.EmailOutbox.Payload.v1");
        _logger = logger;
        _leaseDuration = TimeSpan.FromSeconds(Math.Clamp(configured.LeaseDurationSeconds, 30, 3_600));
        _maxAttempts = Math.Clamp(configured.MaxAttempts, 1, 100);
        _initialRetryDelaySeconds = Math.Clamp(configured.InitialRetryDelaySeconds, 1, 3_600);
        _maximumRetryDelaySeconds = Math.Clamp(configured.MaximumRetryDelaySeconds, _initialRetryDelaySeconds, 86_400);
    }

    public async Task<bool> DispatchNextAsync(CancellationToken cancellationToken)
    {
        var lease = await TryClaimNextAsync(cancellationToken);
        if (lease is null)
            return false;

        try
        {
            var json = _protector.Unprotect(lease.ProtectedPayload);
            var payload = JsonSerializer.Deserialize<EmailOutboxPayload>(json, SerializerOptions)
                ?? throw new InvalidDataException("The encrypted email payload was empty.");

            await _transport.SendAsync(payload.ToAddress, payload.Subject, payload.Body, cancellationToken);
            await MarkSentAsync(lease, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Keep the lease until expiry. SMTP may have accepted the message
            // just before cancellation, so immediate retry would increase the
            // chance of a duplicate during graceful shutdown.
            throw;
        }
        catch (Exception exception)
        {
            var now = DateTime.UtcNow;
            var deadLettered = lease.AttemptCount >= _maxAttempts;
            var nextAttempt = deadLettered
                ? now
                : now.Add(CalculateRetryDelay(lease.AttemptCount));
            await MarkFailedAsync(lease, now, nextAttempt, exception.GetType().Name, cancellationToken);

            _logger.LogWarning(
                "Email outbox message {MessageId} failed on attempt {AttemptCount} ({FailureType}); {Disposition}.",
                lease.Id,
                lease.AttemptCount,
                exception.GetType().Name,
                deadLettered ? "moved to the dead-letter state" : "scheduled for retry");
        }

        return true;
    }

    private async Task<EmailLease?> TryClaimNextAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var now = DateTime.UtcNow;
        var candidate = await db.EmailOutboxMessages
            .Where(message => message.ProtectedPayload != null &&
                              message.SentAtUtc == null &&
                              message.DeadLetteredAtUtc == null &&
                              message.NextAttemptAtUtc <= now &&
                              (message.LeaseExpiresAtUtc == null || message.LeaseExpiresAtUtc <= now))
            .OrderBy(message => message.NextAttemptAtUtc)
            .ThenBy(message => message.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (candidate is null)
            return null;

        var leaseId = Guid.NewGuid();
        var leaseExpiresAtUtc = now.Add(_leaseDuration);
        if (db.Database.IsRelational())
        {
            var previousAttemptCount = candidate.AttemptCount;
            var claimed = await db.EmailOutboxMessages
                .Where(message => message.Id == candidate.Id &&
                                  message.AttemptCount == previousAttemptCount &&
                                  message.ProtectedPayload != null &&
                                  message.SentAtUtc == null &&
                                  message.DeadLetteredAtUtc == null &&
                                  message.NextAttemptAtUtc <= now &&
                                  (message.LeaseExpiresAtUtc == null || message.LeaseExpiresAtUtc <= now))
                .ExecuteUpdateAsync(update => update
                    .SetProperty(message => message.LeaseId, leaseId)
                    .SetProperty(message => message.LeaseExpiresAtUtc, leaseExpiresAtUtc)
                    .SetProperty(message => message.AttemptCount, message => message.AttemptCount + 1),
                    cancellationToken);

            if (claimed != 1 || candidate.ProtectedPayload is null)
                return null;

            return new EmailLease(candidate.Id, leaseId, candidate.ProtectedPayload, previousAttemptCount + 1);
        }

        candidate.Claim(leaseId, leaseExpiresAtUtc);
        await db.SaveChangesAsync(cancellationToken);
        return new EmailLease(candidate.Id, leaseId, candidate.ProtectedPayload!, candidate.AttemptCount);
    }

    private async Task MarkSentAsync(EmailLease lease, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var now = DateTime.UtcNow;

        if (db.Database.IsRelational())
        {
            var updated = await db.EmailOutboxMessages
                .Where(message => message.Id == lease.Id && message.LeaseId == lease.LeaseId && message.SentAtUtc == null)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(message => message.ProtectedPayload, (string?)null)
                    .SetProperty(message => message.LeaseId, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(message => message.LastFailureType, (string?)null)
                    .SetProperty(message => message.SentAtUtc, now), cancellationToken);

            if (updated == 0)
                _logger.LogWarning("Email outbox lease {MessageId} expired before its sent marker was recorded.", lease.Id);
            return;
        }

        var message = await db.EmailOutboxMessages.SingleOrDefaultAsync(item => item.Id == lease.Id, cancellationToken);
        if (message is null || !message.MarkSent(lease.LeaseId, now))
        {
            _logger.LogWarning("Email outbox lease {MessageId} expired before its sent marker was recorded.", lease.Id);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        EmailLease lease,
        DateTime nowUtc,
        DateTime nextAttemptAtUtc,
        string failureType,
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var deadLettered = lease.AttemptCount >= _maxAttempts;

        if (db.Database.IsRelational())
        {
            var query = db.EmailOutboxMessages
                .Where(message => message.Id == lease.Id && message.LeaseId == lease.LeaseId && message.SentAtUtc == null);
            var updated = deadLettered
                ? await query.ExecuteUpdateAsync(update => update
                    .SetProperty(message => message.LeaseId, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(message => message.LastFailureType, failureType)
                    .SetProperty(message => message.DeadLetteredAtUtc, nowUtc), cancellationToken)
                : await query.ExecuteUpdateAsync(update => update
                    .SetProperty(message => message.LeaseId, (Guid?)null)
                    .SetProperty(message => message.LeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(message => message.LastFailureType, failureType)
                    .SetProperty(message => message.NextAttemptAtUtc, nextAttemptAtUtc), cancellationToken);

            if (updated == 0)
                _logger.LogWarning("Email outbox lease {MessageId} expired before its failure state was recorded.", lease.Id);
            return;
        }

        var message = await db.EmailOutboxMessages.SingleOrDefaultAsync(item => item.Id == lease.Id, cancellationToken);
        if (message is null || !message.MarkFailed(lease.LeaseId, nowUtc, nextAttemptAtUtc, _maxAttempts, failureType))
        {
            _logger.LogWarning("Email outbox lease {MessageId} expired before its failure state was recorded.", lease.Id);
            return;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 30);
        var seconds = Math.Min(_maximumRetryDelaySeconds, _initialRetryDelaySeconds * Math.Pow(2, exponent));
        return TimeSpan.FromSeconds(seconds);
    }

    private sealed record EmailLease(Guid Id, Guid LeaseId, string ProtectedPayload, int AttemptCount);
}
