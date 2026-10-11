using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;

namespace Mk8.Drava.IntegrationTests;

// This process-local runtime source includes manually constructed SDK channels.
// Capture only fixed event identity, numeric HTTP status and monotonic timing;
// never retain payload strings containing URLs, headers or exception details.
internal sealed class DevelopmentRegistrationEvents : EventListener
{
    private readonly ConcurrentQueue<string> _events = new();
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _count;

    internal string[] Read() => [.. _events];

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (string.Equals(eventSource.Name, "System.Net.Http", StringComparison.Ordinal))
            EnableEvents(eventSource, EventLevel.Informational, EventKeywords.None);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        // EventListener can call overrides during its base constructor, before
        // the derived queue exists. Pre-construction events are not observations.
        if (_events is null || !string.Equals(eventData.EventSource.Name, "System.Net.Http", StringComparison.Ordinal) ||
            eventData.EventId is < 1 or > 14 || Interlocked.Increment(ref _count) > 256)
        {
            return;
        }
        var status = eventData.EventId is 2 or 12 && eventData.Payload is { Count: > 0 } payload && payload[0] is int value ? value : (int?)null;
        _events.Enqueue(string.Create(CultureInfo.InvariantCulture, $"registration_http: elapsed_ms={Stopwatch.GetElapsedTime(_started).TotalMilliseconds}; event={eventData.EventId}; activity={eventData.ActivityId}; status={status}"));
    }
}
