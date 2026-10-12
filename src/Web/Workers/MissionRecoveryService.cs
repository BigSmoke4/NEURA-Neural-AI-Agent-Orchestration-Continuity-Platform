using Microsoft.EntityFrameworkCore;
using Neura.Infrastructure.Persistence;
using Neura.Modules.Handoff.Domain;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.Orchestration.Domain;

namespace Neura.Web.Workers;

/// <summary>
/// Maintains a database-backed liveness timestamp while a mission is executing
/// and conservatively closes runs whose worker stopped renewing that timestamp.
/// A stale mission is marked failed rather than re-queued: provider APIs do not
/// offer a general idempotency guarantee for arbitrary mission requests.
/// </summary>
public sealed class MissionRecoveryService
{
    private const string StaleRunReason =
        "The worker heartbeat expired. The mission was marked failed and was not automatically retried because provider-side effects may already have occurred.";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MissionRecoveryService> _logger;
    private readonly INeuralEventPublisher _events;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _staleRunningTimeout;
    private readonly TimeSpan _recoverySweepInterval;
    private readonly int _maxMissionsPerSweep;

    public MissionRecoveryService(
        IServiceScopeFactory scopeFactory,
        ILogger<MissionRecoveryService> logger,
        INeuralEventPublisher events,
        MissionRecoveryOptions options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _events = events;

        var heartbeatSeconds = Math.Clamp(options.HeartbeatIntervalSeconds, 1, 300);
        _heartbeatInterval = TimeSpan.FromSeconds(heartbeatSeconds);
        _staleRunningTimeout = TimeSpan.FromSeconds(
            Math.Clamp(options.StaleRunningTimeoutSeconds, heartbeatSeconds * 3, 86_400));
        _recoverySweepInterval = TimeSpan.FromSeconds(Math.Clamp(options.RecoverySweepIntervalSeconds, 1, 3_600));
        _maxMissionsPerSweep = Math.Clamp(options.MaxMissionsPerSweep, 1, 500);
    }

    public TimeSpan RecoverySweepInterval => _recoverySweepInterval;

    /// <summary>
    /// Executes provider work while periodically updating Mission.UpdatedAtUtc.
    /// If another worker/recovery sweep changes the mission out of Running, the
    /// provider operation is cancelled and its result is not committed locally.
    /// </summary>
    public async Task<T> RunWithHeartbeatAsync<T>(
        Guid missionId,
        Func<CancellationToken, Task<T>> runAsync,
        CancellationToken cancellationToken)
    {
        using var executionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Verify/renew ownership before the first provider call. A worker that
        // was blocked before orchestration must not start external work after a
        // recovery sweep has already made the mission terminal.
        await RefreshHeartbeatAsync(missionId, executionCts.Token);
        var heartbeatTask = MaintainHeartbeatAsync(missionId, executionCts.Token);
        var executionTask = InvokeAsync(runAsync, executionCts.Token);
        await Task.WhenAny(executionTask, heartbeatTask);

        if (heartbeatTask.IsCompleted)
        {
            try
            {
                // The heartbeat is intentionally infinite. A normal completion
                // is therefore treated as a lost lease, not successful work.
                await heartbeatTask;
                throw new MissionRunNoLongerActiveException(missionId);
            }
            finally
            {
                executionCts.Cancel();
                await ObserveExecutionAsync(executionTask, missionId);
            }
        }

        try
        {
            var result = await executionTask;
            executionCts.Cancel();
            try
            {
                // Surface a lost lease even if it raced with task completion;
                // otherwise a stale tracked Mission could overwrite recovery.
                await heartbeatTask;
            }
            catch (OperationCanceledException) when (executionCts.IsCancellationRequested)
            {
                // Normal stop after the orchestration task has completed.
            }

            return result;
        }
        catch (Exception)
        {
            executionCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // Normal stop after failure or host cancellation.
            }
            catch (MissionRunNoLongerActiveException)
            {
                // Prefer the state-change signal so the worker does not persist
                // a stale local failure over the recovery decision.
                throw;
            }
            catch (Exception heartbeatError)
            {
                _logger.LogWarning(heartbeatError,
                    "Mission {MissionId} heartbeat also failed while provider execution was stopping.", missionId);
            }

            throw;
        }
    }

    /// <summary>
    /// Fails stale Running missions in bounded batches. Relational state changes
    /// are compare-and-set operations inside a transaction so concurrent recovery
    /// workers cannot overwrite a live heartbeat or leave tasks/handoffs half
    /// recovered. In-memory providers use the tracked-domain path for tests.
    /// </summary>
    public async Task<int> RecoverStaleRunningMissionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var staleBeforeUtc = now - _staleRunningTimeout;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();

        var staleMissions = await db.Missions.AsNoTracking()
            .Where(m => m.Status == MissionStatus.Running && m.UpdatedAtUtc < staleBeforeUtc)
            .OrderBy(m => m.UpdatedAtUtc)
            .Take(_maxMissionsPerSweep)
            .Select(m => new { m.Id, m.OwnerUserId })
            .ToListAsync(cancellationToken);

        var recoveredCount = 0;
        foreach (var stale in staleMissions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var taskId = await db.Tasks.AsNoTracking()
                .Where(t => t.MissionId == stale.Id)
                .OrderBy(t => t.Order)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            bool recovered;
            if (db.Database.IsRelational())
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                recovered = await RecoverRelationalMissionAsync(db, stale.Id, staleBeforeUtc, now, cancellationToken);
                if (recovered)
                    await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                recovered = await RecoverTrackedMissionAsync(db, stale.Id, staleBeforeUtc, cancellationToken);
            }

            if (!recovered)
                continue;

            recoveredCount++;
            _logger.LogWarning(
                "Marked stale mission {MissionId} as failed without retry; its last heartbeat was before {StaleBeforeUtc}.",
                stale.Id,
                staleBeforeUtc);

            await _events.PublishAsync(NeuralEventTypes.MissionFailed, new
            {
                MissionId = stale.Id,
                OwnerUserId = stale.OwnerUserId,
                TaskId = taskId,
                Error = StaleRunReason,
                RecoveryAction = "NotRetried"
            }, cancellationToken);
        }

        return recoveredCount;
    }

    private async Task MaintainHeartbeatAsync(Guid missionId, CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(_heartbeatInterval, cancellationToken);
            await RefreshHeartbeatAsync(missionId, cancellationToken);
        }
    }

    private async Task RefreshHeartbeatAsync(Guid missionId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NeuraDbContext>();
        var now = DateTime.UtcNow;

        if (db.Database.IsRelational())
        {
            var renewed = await db.Missions
                .Where(m => m.Id == missionId && m.Status == MissionStatus.Running)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

            if (renewed != 1)
                throw new MissionRunNoLongerActiveException(missionId);
        }
        else
        {
            var mission = await db.Missions.FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);
            if (mission is null || mission.Status != MissionStatus.Running)
                throw new MissionRunNoLongerActiveException(missionId);

            mission.RefreshExecutionHeartbeat();
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<bool> RecoverRelationalMissionAsync(
        NeuraDbContext db,
        Guid missionId,
        DateTime staleBeforeUtc,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var changed = await db.Missions
            .Where(m => m.Id == missionId && m.Status == MissionStatus.Running && m.UpdatedAtUtc < staleBeforeUtc)
            .ExecuteUpdateAsync(update => update
                .SetProperty(m => m.Status, MissionStatus.Failed)
                .SetProperty(m => m.UpdatedAtUtc, now), cancellationToken);

        if (changed != 1)
            return false;

        await db.Tasks
            .Where(t => t.MissionId == missionId &&
                        t.Status != TaskStatus2.Completed &&
                        t.Status != TaskStatus2.Failed &&
                        t.Status != TaskStatus2.Cancelled)
            .ExecuteUpdateAsync(update => update
                .SetProperty(t => t.Status, TaskStatus2.Failed)
                .SetProperty(t => t.UpdatedAtUtc, now), cancellationToken);

        await db.Handoffs
            .Where(h => h.MissionId == missionId &&
                        h.State != HandoffState.Completed &&
                        h.State != HandoffState.Rejected &&
                        h.State != HandoffState.Failed)
            .ExecuteUpdateAsync(update => update
                .SetProperty(h => h.State, HandoffState.Failed)
                .SetProperty(h => h.ValidationNotes, StaleRunReason), cancellationToken);

        return true;
    }

    private static async Task<bool> RecoverTrackedMissionAsync(
        NeuraDbContext db,
        Guid missionId,
        DateTime staleBeforeUtc,
        CancellationToken cancellationToken)
    {
        var mission = await db.Missions.FirstOrDefaultAsync(m => m.Id == missionId, cancellationToken);
        if (mission is null || mission.Status != MissionStatus.Running || mission.UpdatedAtUtc >= staleBeforeUtc)
            return false;

        var tasks = await db.Tasks.Where(t => t.MissionId == missionId).ToListAsync(cancellationToken);
        var handoffs = await db.Handoffs.Where(h => h.MissionId == missionId).ToListAsync(cancellationToken);

        mission.SetStatus(MissionStatus.Failed);
        foreach (var task in tasks.Where(t => t.Status is not (TaskStatus2.Completed or TaskStatus2.Failed or TaskStatus2.Cancelled)))
            task.Fail();
        foreach (var handoff in handoffs.Where(h => h.State is not (HandoffState.Completed or HandoffState.Rejected or HandoffState.Failed)))
            handoff.Fail(StaleRunReason);

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ObserveExecutionAsync<T>(Task<T> executionTask, Guid missionId)
    {
        try
        {
            await executionTask;
        }
        catch (OperationCanceledException)
        {
            // Expected when a heartbeat is lost or the host is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Mission {MissionId} execution stopped after heartbeat cancellation.", missionId);
        }
    }

    private static async Task<T> InvokeAsync<T>(Func<CancellationToken, Task<T>> runAsync, CancellationToken cancellationToken)
        => await runAsync(cancellationToken);
}

public sealed class MissionRunNoLongerActiveException : Exception
{
    public MissionRunNoLongerActiveException(Guid missionId)
        : base($"The worker no longer owns the running state for mission {missionId}.")
    {
        MissionId = missionId;
    }

    public Guid MissionId { get; }
}
