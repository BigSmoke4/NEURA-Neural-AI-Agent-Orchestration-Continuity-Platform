using System.Net.Http.Json;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.ProviderIntegration.Infrastructure;

/// <summary>
/// Real-mode adapter for Google's official Gemini generateContent API.
/// Same NOT CONFIGURED contract as the other real adapters.
/// </summary>
public sealed class GoogleAIProvider : IAIProvider
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    private readonly ModelPricingOptions? _pricing;

    public GoogleAIProvider(HttpClient http, string? apiKey, ModelPricingOptions? pricing = null)
    {
        _http = http;
        _apiKey = apiKey;
        _pricing = pricing;
        _http.BaseAddress ??= new Uri("https://generativelanguage.googleapis.com/");
    }

    public ProviderKind Kind => ProviderKind.Google;
    public bool IsSimulation => false;

    public async Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new ProviderNotConfiguredException("Google");

        var started = DateTime.UtcNow;
        var url = $"v1beta/models/{Uri.EscapeDataString(request.ModelId)}:generateContent";
        var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Headers.Add("x-goog-api-key", _apiKey);
        req.Content = JsonContent.Create(new
        {
            contents = request.Messages.Select(m => new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            })
        });

        using var response = await _http.SendAsync(req, cancellationToken);
        var latency = DateTime.UtcNow - started;

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return new AIResponse(request.AiRequestId, string.Empty, new AITokenUsage(0, 0, 0, 0), latency, 0m, false, body);
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleResponsePayload>(cancellationToken: cancellationToken);
        var text = payload?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        var usage = new AITokenUsage(
            payload?.UsageMetadata?.PromptTokenCount ?? 0,
            payload?.UsageMetadata?.CandidatesTokenCount ?? 0,
            payload?.UsageMetadata?.TotalTokenCount ?? 0,
            1000000);

        var price = _pricing?.GetOrDefault(request.ModelId, 0.00125m, 0.005m) ?? new ModelPrice { InputPer1k = 0.00125m, OutputPer1k = 0.005m };
        var capabilities = new AIModelCapabilities(request.ModelId, 1000000, true, true, price.InputPer1k, price.OutputPer1k);
        var cost = CostCalculator.Estimate(usage, capabilities);
        return new AIResponse(request.AiRequestId, text, usage, latency, cost, true, null);
    }

    public Task<AIModelCapabilities> GetCapabilitiesAsync(string modelId, CancellationToken cancellationToken)
    {
        var price = _pricing?.GetOrDefault(modelId, 0.00125m, 0.005m)
            ?? new ModelPrice { InputPer1k = 0.00125m, OutputPer1k = 0.005m };
        return Task.FromResult(new AIModelCapabilities(
            modelId, 1000000, true, true, price.InputPer1k, price.OutputPer1k));
    }

    public Task<ProviderHealth> GetHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(string.IsNullOrEmpty(_apiKey)
            ? new ProviderHealth(false, "NOT CONFIGURED", null)
            : new ProviderHealth(true, "OK", TimeSpan.Zero));

    private sealed class GoogleResponsePayload
    {
        public List<GoogleCandidate>? Candidates { get; set; }
        public GoogleUsage? UsageMetadata { get; set; }
    }
    private sealed class GoogleCandidate { public GoogleContent? Content { get; set; } }
    private sealed class GoogleContent { public List<GooglePart>? Parts { get; set; } }
    private sealed class GooglePart { public string? Text { get; set; } }
    private sealed class GoogleUsage
    {
        public int PromptTokenCount { get; set; }
        public int CandidatesTokenCount { get; set; }
        public int TotalTokenCount { get; set; }
    }
}
