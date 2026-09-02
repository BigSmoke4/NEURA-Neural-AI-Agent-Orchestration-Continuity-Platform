using System.Net.Http.Json;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Real-mode adapter for OpenAI's official Chat Completions API.
/// Same contract as AnthropicAIProvider: throws ProviderNotConfiguredException
/// (surfaced as NOT CONFIGURED) rather than fabricating output.
/// </summary>
public sealed class OpenAIProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    private readonly ModelPricingOptions? _pricing;

    public OpenAIProvider(HttpClient http, string? apiKey, ModelPricingOptions? pricing = null)
    {
        _http = http;
        _apiKey = apiKey;
        _pricing = pricing;
        _http.BaseAddress ??= new Uri("https://api.openai.com/");
    }

    public ProviderKind Kind => ProviderKind.OpenAI;
    public bool IsSimulation => false;

    public async Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new ProviderNotConfiguredException("OpenAI");

        var started = DateTime.UtcNow;
        var req = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions");
        req.Headers.Add("Authorization", $"Bearer {_apiKey}");
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
            return new AIResponse(request.AiRequestId, string.Empty, new AITokenUsage(0, 0, 0, 0), latency, 0m, false, body);
        }

        var payload = await response.Content.ReadFromJsonAsync<OpenAIResponsePayload>(cancellationToken: cancellationToken);
        var text = payload?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        var usage = new AITokenUsage(
            payload?.Usage?.PromptTokens ?? 0,
            payload?.Usage?.CompletionTokens ?? 0,
            payload?.Usage?.TotalTokens ?? 0,
            128000);

        var price = _pricing?.GetOrDefault(request.ModelId, 0.0025m, 0.01m) ?? new ModelPrice { InputPer1k = 0.0025m, OutputPer1k = 0.01m };
        var capabilities = new AIModelCapabilities(request.ModelId, 128000, true, true, price.InputPer1k, price.OutputPer1k);
        var cost = CostCalculator.Estimate(usage, capabilities);
        return new AIResponse(request.AiRequestId, text, usage, latency, cost, true, null);
    }

    public Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken)
    {
        var price = _pricing?.GetOrDefault(modelId, 0.0025m, 0.01m)
            ?? new ModelPrice { InputPer1k = 0.0025m, OutputPer1k = 0.01m };
        return Task.FromResult(new AIModelCapabilities(
            modelId, 128000, true, true, price.InputPer1k, price.OutputPer1k));
    }

    public Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(string.IsNullOrEmpty(_apiKey)
            ? new ProviderHealth(false, "NOT CONFIGURED", null)
            : new ProviderHealth(true, "OK", TimeSpan.Zero));

    private sealed class OpenAIResponsePayload
    {
        public List<OpenAIChoice>? Choices { get; set; }
        public OpenAIUsage? Usage { get; set; }
    }
    private sealed class OpenAIChoice { public OpenAIMessage? Message { get; set; } }
    private sealed class OpenAIMessage { public string? Content { get; set; } }
    private sealed class OpenAIUsage
    {
        public int PromptTokens { get; set; }
        public int CompletionTokens { get; set; }
        public int TotalTokens { get; set; }
    }
}
