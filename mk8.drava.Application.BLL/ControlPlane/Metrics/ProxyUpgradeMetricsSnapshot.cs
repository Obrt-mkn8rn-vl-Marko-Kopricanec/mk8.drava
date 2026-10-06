namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpgradeMetricsSnapshot(long RequestsReceived, long RequestsSucceeded, long RequestsRejected, long UpstreamFailures);
