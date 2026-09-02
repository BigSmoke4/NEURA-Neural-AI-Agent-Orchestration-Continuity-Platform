namespace Neura.Modules.ProviderIntegration.Domain;

/// <summary>
/// Provider-agnostic execution abstraction. Concrete adapters
/// (OpenAIProvider, AnthropicProvider, GoogleProvider, LocalModelProvider,
/// SimulationProvider) implement this against each vendor's official API.
/// No credential scraping, no browser automation, no password storage.
/// </summary>
public interface IAIProvider
{
    ProviderKind Kind { get; }
    bool IsSimulation { get; }

    Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken);
    Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken);
    Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken);
}
