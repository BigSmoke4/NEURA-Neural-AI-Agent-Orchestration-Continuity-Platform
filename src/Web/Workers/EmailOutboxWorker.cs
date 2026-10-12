using Microsoft.Extensions.Options;
using Neura.Infrastructure.Email;
using Neura.Infrastructure.Persistence;

namespace Neura.Web.Workers;

/// <summary>Drains the encrypted SMTP outbox without blocking request handling.</summary>
public sealed class EmailOutboxWorker : BackgroundService
{
    private readonly IEmailOutboxDispatcher _dispatcher;
    private readonly ILogger<EmailOutboxWorker> _logger;
    private readonly TimeSpan _pollInterval;

    public EmailOutboxWorker(
        IEmailOutboxDispatcher dispatcher,
        IOptions<EmailOutboxOptions> options,
        ILogger<EmailOutboxWorker> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
        _pollInterval = TimeSpan.FromSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 1, 300));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await _dispatcher.DispatchNextAsync(stoppingToken);
                if (!processed)
                    await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Avoid logging exception messages: provider/SMTP implementations
                // can include remote response bodies or deployment details.
                _logger.LogError(
                    "Email outbox worker failed with {FailureType}; it will retry after {DelaySeconds} seconds.",
                    exception.GetType().Name,
                    _pollInterval.TotalSeconds);
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }
    }
}
