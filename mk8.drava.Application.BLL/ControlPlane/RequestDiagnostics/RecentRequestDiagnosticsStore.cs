namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public sealed class RecentRequestDiagnosticsStore : IProxyRequestDiagnosticsSource
{
    public const int MaximumReadLimit = 500;
    private readonly IProxyRequestDiagnosticsMetricsSink _metrics;
    private readonly Lock _gate = new();
    private readonly LinkedList<ProxyRecentRequestDiagnosticEvent> _events = new();
    public RecentRequestDiagnosticsStore(IProxyRequestDiagnosticsMetricsSink metrics)
    {
        _metrics = metrics;
    }

    public void Add(ProxyRecentRequestDiagnosticEvent diagnostic, int capacity)
    {
        lock (_gate)
        {
            var boundedCapacity = Math.Max(1, capacity);
            _events.AddFirst(diagnostic);
            while (_events.Count > boundedCapacity)
            {
                _events.RemoveLast();
                _metrics.RecentDiagnosticOverwritten();
            }
        }
    }

    public IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Recent(int limit)
    {
        var boundedLimit = Math.Clamp(limit, 1, MaximumReadLimit);
        List<ProxyRecentRequestDiagnosticEvent> results = [];
        lock (_gate)
        {
            var current = _events.First;
            while (current is not null && results.Count < boundedLimit)
            {
                results.Add(current.Value);
                current = current.Next;
            }
        }

        return results.AsReadOnly();
    }
}
