namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamHttp3MetricsSnapshot(long Requests, long ConnectionAttempts, long ConnectionSuccesses, long ConnectionFailures, long PoolConnectionsOpened, long PoolConnectionsReused, long PoolConnectionsClosed, long StreamLimitRejections, long ActiveConnections, long ActiveStreams, IReadOnlyDictionary<string, long> ProtocolErrors)
{
    public IReadOnlyDictionary<string, long> ProtocolErrors { get; } = MetricsList.CopyCounterDictionary(ProtocolErrors, StringComparer.Ordinal);
}
