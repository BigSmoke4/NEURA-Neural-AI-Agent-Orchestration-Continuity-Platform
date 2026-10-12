using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Neura.Modules.Orchestration.Application;
using Neura.Modules.ProviderIntegration.Domain;
using Xunit;

namespace Neura.Tests.Observability;

public sealed class OrchestrationMetricsTests
{
    [Fact]
    public void Provider_metrics_use_bounded_dimensions_and_keep_simulation_explicit()
    {
        var requestDimensions = new ConcurrentBag<Dictionary<string, string>>();
        var metricDimensions = new ConcurrentBag<Dictionary<string, string>>();
        var requestDurations = new ConcurrentBag<double>();
        var inputTokenMeasurements = new ConcurrentBag<long>();
        var outputTokenMeasurements = new ConcurrentBag<long>();
        var contextUsageMeasurements = new ConcurrentBag<double>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == OrchestrationMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            metricDimensions.Add(CopyTags(tags));
            if (instrument.Name == "neura.provider.execution.attempts")
                requestDimensions.Add(CopyTags(tags));
            else if (instrument.Name == "neura.provider.tokens.input")
                inputTokenMeasurements.Add(measurement);
            else if (instrument.Name == "neura.provider.tokens.output")
                outputTokenMeasurements.Add(measurement);
        });
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
        {
            metricDimensions.Add(CopyTags(tags));
            if (instrument.Name == "neura.provider.execution.duration")
                requestDurations.Add(measurement);
            else if (instrument.Name == "neura.provider.context.usage_ratio")
                contextUsageMeasurements.Add(measurement);
        });
        listener.Start();

        OrchestrationMetrics.RecordProviderExecution(ProviderKind.OpenAI, false, "success", 12.5);
        OrchestrationMetrics.RecordTokenUsage(ProviderKind.OpenAI, false, 123, 45, 1_000, 0.168);
        OrchestrationMetrics.RecordProviderExecution(ProviderKind.Simulation, true, "success", 2.0);
        OrchestrationMetrics.RecordProviderExecution((ProviderKind)999, false, "untrusted-outcome", 3.0);

        Assert.Contains(requestDimensions, tags =>
            tags.TryGetValue("provider.name", out var provider) && provider == "openai" &&
            tags.TryGetValue("neura.mode", out var mode) && mode == "real" &&
            tags.TryGetValue("execution.outcome", out var outcome) && outcome == "success");
        Assert.Contains(requestDimensions, tags =>
            tags.TryGetValue("provider.name", out var provider) && provider == "simulation" &&
            tags.TryGetValue("neura.mode", out var mode) && mode == "simulation");
        Assert.Contains(requestDimensions, tags =>
            tags.TryGetValue("provider.name", out var provider) && provider == "other" &&
            tags.TryGetValue("execution.outcome", out var outcome) && outcome == "other");
        Assert.All(requestDimensions, tags => Assert.DoesNotContain("untrusted-outcome", tags.Values));
        var allowedDimensionNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "provider.name",
            "neura.mode",
            "execution.outcome"
        };
        Assert.All(metricDimensions, tags =>
        {
            Assert.Contains("provider.name", tags.Keys);
            Assert.Contains("neura.mode", tags.Keys);
            Assert.All(tags.Keys, key => Assert.Contains(key, allowedDimensionNames));
            Assert.DoesNotContain("untrusted-outcome", tags.Values);
        });
        Assert.Contains(12.5, requestDurations);
        Assert.Contains(123L, inputTokenMeasurements);
        Assert.Contains(45L, outputTokenMeasurements);
        Assert.Contains(contextUsageMeasurements, ratio => Math.Abs(ratio - 0.168) < 0.000001);
    }

    private static Dictionary<string, string> CopyTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (tag.Value is not null)
                copy[tag.Key] = tag.Value.ToString() ?? string.Empty;
        }
        return copy;
    }
}
