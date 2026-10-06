namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyHealthMetricsSnapshot(long NoHealthyUpstreamFailures, long ChecksAttempted, long ChecksSucceeded, long ChecksFailed, long UpstreamTransitions);
