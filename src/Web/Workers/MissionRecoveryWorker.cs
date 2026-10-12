namespace Neura.Web.Workers;

/// <summary>
/// Runs stale-mission recovery independently from MissionWorker so a long
/// provider call cannot delay cleanup of another crashed worker's mission.
/// </summary>
public sealed class MissionRecoveryWorker : BackgroundService
{
    private readonly MissionRecoveryService _recovery;
    private readonly ILogger<MissionRecoveryWorker> _logger;

    public MissionRecoveryWorker(
        MissionRecoveryService recovery,
        ILogger<MissionRecoveryWorker> logger)
    {
        _recovery = recovery;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _recovery.RecoverStaleRunningMissionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stale mission recovery sweep failed.");
            }

            try
            {
                await Task.Delay(_recovery.RecoverySweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
