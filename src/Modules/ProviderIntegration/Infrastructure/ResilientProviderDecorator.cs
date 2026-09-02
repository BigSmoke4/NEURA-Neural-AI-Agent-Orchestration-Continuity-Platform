using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Section 15/45: retries with exponential backoff and a simple circuit
/// breaker wrapped around any IAIProvider. Prevents infinite retry loops
/// and stops calling a provider that keeps failing until it cools down.
/// </summary>
public sealed class ResilientProviderDecorator : IAIProvider
{
    private readonly IAIProvider _inner;
    private readonly int _maxRetries;
    private readonly TimeSpan _baseDelay;
    private readonly int _circuitFailureThreshold;
    private readonly TimeSpan _circuitResetAfter;

    private int _consecutiveFailures;
    private DateTime? _circuitOpenedAtUtc;

    public ResilientProviderDecorator(IAIProvider inner, int maxRetries = 3,
        TimeSpan? baseDelay = null, int circuitFailureThreshold = 5, TimeSpan? circuitResetAfter = null)
    {
        _inner = inner;
        _maxRetries = maxRetries;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(250);
        _circuitFailureThreshold = circuitFailureThreshold;
        _circuitResetAfter = circuitResetAfter ?? TimeSpan.FromSeconds(30);
    }

    public ProviderKind Kind => _inner.Kind;
    public bool IsSimulation => _inner.IsSimulation;

    private bool IsCircuitOpen()
    {
        if (_circuitOpenedAtUtc is null) return false;
        if (DateTime.UtcNow - _circuitOpenedAtUtc.Value > _circuitResetAfter)
        {
            _circuitOpenedAtUtc = null; // half-open: allow next attempt through
            _consecutiveFailures = 0;
            return false;
        }
        return true;
    }

    public async Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken)
    {
        if (IsCircuitOpen())
        {
            return new AIResponse(request.AiRequestId, string.Empty, new AITokenUsage(0, 0, 0, 0),
                TimeSpan.Zero, 0m, false, $"Circuit open for provider {Kind}: too many recent failures.");
        }

        Exception? lastError = null;
        for (int attempt = 0; attempt <= _maxRetries; attempt++)
        {
            try
            {
                var response = await _inner.ExecuteAsync(request, cancellationToken);
                if (response.IsSuccess)
                {
                    _consecutiveFailures = 0;
                    return response;
                }
                lastError = new Exception(response.ErrorMessage);
            }
            catch (Exception ex)
            {
                lastError = ex;
            }

            if (attempt < _maxRetries)
                await Task.Delay(_baseDelay * Math.Pow(2, attempt), cancellationToken);
        }

        _consecutiveFailures++;
        if (_consecutiveFailures >= _circuitFailureThreshold)
            _circuitOpenedAtUtc = DateTime.UtcNow;

        return new AIResponse(request.AiRequestId, string.Empty, new AITokenUsage(0, 0, 0, 0),
            TimeSpan.Zero, 0m, false, lastError?.Message ?? "Unknown provider failure after retries.");
    }

    public Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken)
        => _inner.GetCapabilitiesAsync(modelId, cancellationToken);

    public Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken)
        => _inner.GetHealthAsync(cancellationToken);
}
