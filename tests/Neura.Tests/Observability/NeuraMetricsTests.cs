using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Neura.Modules.Orchestration.Application;
using Neura.Web.Observability;
using Xunit;

namespace Neura.Tests.Observability;

public sealed class NeuraMetricsTests
{
    [Fact]
    public void Persisted_event_metrics_use_only_bounded_event_type_tags()
    {
        var observedEventTypes = new ConcurrentBag<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == NeuraMetrics.MeterName)
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name != "neura.events.persisted")
                return;

            foreach (var tag in tags)
            {
                if (tag.Key == "event.type" && tag.Value is string value)
                    observedEventTypes.Add(value);
            }
        });
        listener.Start();

        NeuraMetrics.RecordEventPersisted(NeuralEventTypes.MissionCompleted);
        NeuraMetrics.RecordEventPersisted("untrusted-user-controlled-value");

        Assert.Contains(NeuralEventTypes.MissionCompleted, observedEventTypes);
        Assert.Contains("other", observedEventTypes);
        Assert.DoesNotContain("untrusted-user-controlled-value", observedEventTypes);
    }
}
