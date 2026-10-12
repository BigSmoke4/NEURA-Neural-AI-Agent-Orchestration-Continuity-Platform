using Neura.Modules.Memory.Application;

namespace Neura.Web.Workers;

/// <summary>
/// Removes expired project memories in bounded batches. Retrieval also filters
/// expired records, so cleanup delay never makes an expired item available to
/// orchestration again.
/// </summary>
public sealed class MemoryExpirationWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(1);
    private const int BatchSize = 500;
    private const int MaxBatchesPerSweep = 10;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MemoryExpirationWorker> _logger;

    public MemoryExpirationWorker(IServiceScopeFactory scopeFactory, ILogger<MemoryExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = 0;
                for (var batch = 0; batch < MaxBatchesPerSweep; batch++)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var memories = scope.ServiceProvider.GetRequiredService<IMemoryService>();
                    var count = await memories.PurgeExpiredAsync(DateTime.UtcNow, BatchSize, stoppingToken);
                    deleted += count;
                    if (count < BatchSize)
                        break;
                }

                if (deleted > 0)
                    _logger.LogInformation("Purged {ExpiredMemoryCount} expired memory records.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Expired-memory cleanup sweep failed.");
            }

            try
            {
                await Task.Delay(SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
