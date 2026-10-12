using System.Diagnostics.Metrics;
using Neura.Modules.ProviderIntegration.Domain;

namespace Neura.Modules.Orchestration.Application;

/// <summary>
/// Low-cardinality metrics for provider executions observed by orchestration.
/// Outcomes represent adapter attempts, not guaranteed upstream HTTP exchanges;
/// simulation mode is explicitly labeled. Provider and mode labels are bounded,
/// and model, mission, task, owner, prompt, and credential values are excluded.
/// </summary>
public static class OrchestrationMetrics
{
    public const string MeterName = "Neura.Orchestration";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> ProviderExecutionAttempts =
        Meter.CreateCounter<long>("neura.provider.execution.attempts", "{execution}");
    private static readonly Histogram<double> ProviderExecutionDuration =
        Meter.CreateHistogram<double>("neura.provider.execution.duration", "ms");
    private static readonly Counter<long> InputTokens =
        Meter.CreateCounter<long>("neura.provider.tokens.input", "{token}");
    private static readonly Counter<long> OutputTokens =
        Meter.CreateCounter<long>("neura.provider.tokens.output", "{token}");
    private static readonly Histogram<double> ContextUsageRatio =
        Meter.CreateHistogram<double>("neura.provider.context.usage_ratio", "1");

    public static void RecordProviderExecution(
        ProviderKind provider,
        bool isSimulation,
        string outcome,
        double durationMilliseconds)
    {
        var tags = ProviderTags(provider, isSimulation, NormalizeOutcome(outcome));
        ProviderExecutionAttempts.Add(1, tags);
        ProviderExecutionDuration.Record(
            double.IsFinite(durationMilliseconds) ? Math.Max(0, durationMilliseconds) : 0,
            tags);
    }

    public static void RecordTokenUsage(
        ProviderKind provider,
        bool isSimulation,
        int inputTokens,
        int outputTokens,
        int contextWindow,
        double usageRatio)
    {
        var tags = ProviderTags(provider, isSimulation);
        if (inputTokens > 0)
            InputTokens.Add(inputTokens, tags);
        if (outputTokens > 0)
            OutputTokens.Add(outputTokens, tags);
        if (contextWindow > 0 && double.IsFinite(usageRatio))
            ContextUsageRatio.Record(Math.Clamp(usageRatio, 0d, 1d), tags);
    }

    private static KeyValuePair<string, object?>[] ProviderTags(
        ProviderKind provider,
        bool isSimulation,
        string? outcome = null)
    {
        var tags = new List<KeyValuePair<string, object?>>(outcome is null ? 2 : 3)
        {
            new("provider.name", NormalizeProvider(provider)),
            new("neura.mode", isSimulation ? "simulation" : "real")
        };
        if (outcome is not null)
            tags.Add(new KeyValuePair<string, object?>("execution.outcome", outcome));
        return tags.ToArray();
    }

    private static string NormalizeProvider(ProviderKind provider)
        => provider switch
        {
            ProviderKind.OpenAI => "openai",
            ProviderKind.Anthropic => "anthropic",
            ProviderKind.Google => "google",
            ProviderKind.LocalModel => "local",
            ProviderKind.Simulation => "simulation",
            _ => "other"
        };

    private static string NormalizeOutcome(string outcome)
        => outcome switch
        {
            "success" => "success",
            "failure" => "failure",
            "cancelled" => "cancelled",
            _ => "other"
        };
}
