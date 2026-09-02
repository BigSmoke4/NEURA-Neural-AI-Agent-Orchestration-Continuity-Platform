using System.Net.Http.Json;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Real-mode adapter calling Anthropic's official Messages API.
/// Requires a connected AIProviderAccount with a protected API key;
/// throws ProviderNotConfiguredException otherwise. No scraping, no
/// stored passwords, official REST endpoint only.
/// </summary>
public sealed class AnthropicAIProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    private readonly ModelPricingOptions? _pricing;

    public AnthropicAIProvider(HttpClient http, string? apiKey, ModelPricingOptions? pricing = null)
    {
        _http = http;
        _apiKey = apiKey;
        _pricing = pricing;
        _http.BaseAddress ??= new Uri("https://api.anthropic.com/");
    }

    public ProviderKind Kind => ProviderKind.Anthropic;
    public bool IsSimulation => false;

    public async Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new ProviderNotConfiguredException("Anthropic");

        var started = DateTime.UtcNow;
        var req = new HttpRequestMessage(HttpMethod.Post, "v1/messages");
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = JsonContent.Create(new
        {
            model = request.ModelId,
            max_tokens = request.MaxOutputTokens ?? 1024,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content })
        });

        using var response = await _http.SendAsync(req, cancellationToken);
        var latency = DateTime.UtcNow - started;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new AIResponse(request.AiRequestId, string.Empty,
                new AITokenUsage(0, 0, 0, 0), latency, 0m, false, body);
        }

        var payload = await response.Content.ReadFromJsonAsync<AnthropicResponsePayload>(cancellationToken: cancellationToken);
        var text = payload?.Content?.FirstOrDefault()?.Text ?? string.Empty;
        var usage = new AITokenUsage(
            payload?.Usage?.InputTokens ?? 0,
            payload?.Usage?.OutputTokens ?? 0,
            (payload?.Usage?.InputTokens ?? 0) + (payload?.Usage?.OutputTokens ?? 0),
            200000); // Claude context window; adjust per model via GetCapabilitiesAsync in production

        var price = _pricing?.GetOrDefault(request.ModelId, 0.003m, 0.015m) ?? new ModelPrice { InputPer1k = 0.003m, OutputPer1k = 0.015m };
        var capabilities = new AIModelCapabilities(request.ModelId, 200000, true, true, price.InputPer1k, price.OutputPer1k);
        var cost = CostCalculator.Estimate(usage, capabilities);
        return new AIResponse(request.AiRequestId, text, usage, latency, cost, true, null);
    }

    public Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken)
    {
        var price = _pricing?.GetOrDefault(modelId, 0.003m, 0.015m)
            ?? new ModelPrice { InputPer1k = 0.003m, OutputPer1k = 0.015m };
        return Task.FromResult(new AIModelCapabilities(
            modelId, 200000, true, true, price.InputPer1k, price.OutputPer1k));
    }

    public async Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
            return new ProviderHealth(false, "NOT CONFIGURED", null);
        return new ProviderHealth(true, "OK", TimeSpan.Zero);
    }

    private sealed class AnthropicResponsePayload
    {
        public List<AnthropicContentBlock>? Content { get; set; }
        public AnthropicUsage? Usage { get; set; }
    }
    private sealed class AnthropicContentBlock { public string? Text { get; set; } }
    private sealed class AnthropicUsage
    {
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
    }
}
