namespace Neura.Infrastructure.Persistence;

/// <summary>
/// Persisted delivery state for encrypted email payloads. Delivery is at-least-once:
/// a process can lose its lease after an SMTP server accepted a message but before
/// the sent marker commits, so consumers must tolerate a duplicate email.
/// </summary>
public sealed class EmailOutboxMessage
{
    private EmailOutboxMessage() { }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public string? ProtectedPayload { get; private set; }
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public DateTime NextAttemptAtUtc { get; private set; } = DateTime.UtcNow;
    public int AttemptCount { get; private set; }
    public Guid? LeaseId { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? SentAtUtc { get; private set; }
    public DateTime? DeadLetteredAtUtc { get; private set; }
    public string? LastFailureType { get; private set; }

    public static EmailOutboxMessage Queue(string protectedPayload, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(protectedPayload))
            throw new ArgumentException("An encrypted email payload is required.", nameof(protectedPayload));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Outbox timestamps must be UTC.", nameof(nowUtc));

        return new EmailOutboxMessage
        {
            ProtectedPayload = protectedPayload,
            CreatedAtUtc = nowUtc,
            NextAttemptAtUtc = nowUtc
        };
    }

    public bool IsClaimableAt(DateTime nowUtc)
        => ProtectedPayload is not null &&
           SentAtUtc is null &&
           DeadLetteredAtUtc is null &&
           NextAttemptAtUtc <= nowUtc &&
           (!LeaseExpiresAtUtc.HasValue || LeaseExpiresAtUtc.Value <= nowUtc);

    public void Claim(Guid leaseId, DateTime leaseExpiresAtUtc)
    {
        if (leaseId == Guid.Empty)
            throw new ArgumentException("A non-empty lease id is required.", nameof(leaseId));
        if (leaseExpiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Lease timestamps must be UTC.", nameof(leaseExpiresAtUtc));
        if (!IsClaimableAt(DateTime.UtcNow))
            throw new InvalidOperationException("Only an eligible unsent email can be claimed.");

        LeaseId = leaseId;
        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        AttemptCount = checked(AttemptCount + 1);
    }

    public bool MarkSent(Guid leaseId, DateTime nowUtc)
    {
        if (LeaseId != leaseId || leaseId == Guid.Empty || nowUtc.Kind != DateTimeKind.Utc)
            return false;

        // Email bodies can contain account-confirmation and password-reset tokens.
        // Erase the ciphertext after successful delivery and retain only metadata.
        ProtectedPayload = null;
        LeaseId = null;
        LeaseExpiresAtUtc = null;
        LastFailureType = null;
        SentAtUtc = nowUtc;
        return true;
    }

    public bool MarkFailed(
        Guid leaseId,
        DateTime nowUtc,
        DateTime nextAttemptAtUtc,
        int maxAttempts,
        string failureType)
    {
        if (LeaseId != leaseId || leaseId == Guid.Empty ||
            nowUtc.Kind != DateTimeKind.Utc || nextAttemptAtUtc.Kind != DateTimeKind.Utc)
        {
            return false;
        }

        LeaseId = null;
        LeaseExpiresAtUtc = null;
        LastFailureType = string.IsNullOrWhiteSpace(failureType)
            ? "UnknownFailure"
            : failureType[..Math.Min(failureType.Length, 200)];

        if (AttemptCount >= Math.Max(1, maxAttempts))
        {
            DeadLetteredAtUtc = nowUtc;
            return true;
        }

        NextAttemptAtUtc = nextAttemptAtUtc;
        return true;
    }
}
