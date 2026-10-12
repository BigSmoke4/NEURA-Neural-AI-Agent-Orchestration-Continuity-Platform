namespace Neura.Infrastructure.Email;

/// <summary>Bounds retry cadence and ownership leases for the durable SMTP outbox.</summary>
public sealed class EmailOutboxOptions
{
    public int PollIntervalSeconds { get; set; } = 5;
    public int LeaseDurationSeconds { get; set; } = 300;
    public int MaxAttempts { get; set; } = 12;
    public int InitialRetryDelaySeconds { get; set; } = 5;
    public int MaximumRetryDelaySeconds { get; set; } = 3_600;
}
