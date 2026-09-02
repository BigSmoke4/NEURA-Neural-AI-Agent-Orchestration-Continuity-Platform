using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Builds a resilient (retry + circuit breaker wrapped) real-mode
/// IAIProvider for a given kind and API key, using named HttpClients
/// from IHttpClientFactory. Keeps the API-key-carrying adapters out of
/// plain DI constructor injection, since the key is per-connected-account,
/// not a singleton app setting.
/// </summary>
public interface IProviderFactory
{
    IAIProvider Create(ProviderKind kind, string? encryptedApiKey);
}

public sealed class ProviderFactory : IProviderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly Func<string, string> _unprotect;
    private readonly ModelPricingOptions? _pricing;

    public ProviderFactory(IHttpClientFactory httpClientFactory, Func<string, string> unprotect, ModelPricingOptions? pricing = null)
    {
        _httpClientFactory = httpClientFactory;
        _unprotect = unprotect;
        _pricing = pricing;
    }

    public IAIProvider Create(ProviderKind kind, string? encryptedApiKey)
    {
        var apiKey = string.IsNullOrEmpty(encryptedApiKey) ? null : _unprotect(encryptedApiKey);

        IAIProvider inner = kind switch
        {
            ProviderKind.Anthropic => new AnthropicAIProvider(_httpClientFactory.CreateClient("anthropic"), apiKey, _pricing),
            ProviderKind.OpenAI => new OpenAIProvider(_httpClientFactory.CreateClient("openai"), apiKey, _pricing),
            ProviderKind.Google => new GoogleAIProvider(_httpClientFactory.CreateClient("google"), apiKey, _pricing),
            ProviderKind.Simulation => new SimulationAIProvider("Simulation"),
            _ => throw new NotSupportedException($"No real-mode adapter registered for {kind}. Implement IAIProvider and add it here.")
        };

        return new ResilientProviderDecorator(inner);
    }
}
