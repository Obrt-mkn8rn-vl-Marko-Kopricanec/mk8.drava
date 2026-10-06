namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyHttp2MetricsSnapshot(long AcceptedConnections, long Requests, long ActiveStreams, IReadOnlyDictionary<string, long> ProtocolErrors)
{
    public IReadOnlyDictionary<string, long> ProtocolErrors { get; } = MetricsList.CopyCounterDictionary(ProtocolErrors, StringComparer.Ordinal);
}
