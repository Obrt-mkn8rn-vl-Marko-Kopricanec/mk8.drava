namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamPoolMetricsSnapshot(long ConnectionsOpened, long ConnectionsReused, long ConnectionsDiscarded, long IdleConnections, long ActiveConnections);
