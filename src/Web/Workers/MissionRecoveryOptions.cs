namespace Neura.Web.Workers;

/// <summary>
/// Controls durable-worker liveness and conservative recovery. A stale run is
/// marked failed, never automatically re-executed, because its provider-side
/// effects may already have happened before the worker stopped heartbeating.
/// </summary>
public sealed class MissionRecoveryOptions
{
    public int HeartbeatIntervalSeconds { get; set; } = 20;
    public int StaleRunningTimeoutSeconds { get; set; } = 300;
    public int RecoverySweepIntervalSeconds { get; set; } = 30;
    public int MaxMissionsPerSweep { get; set; } = 100;
}
