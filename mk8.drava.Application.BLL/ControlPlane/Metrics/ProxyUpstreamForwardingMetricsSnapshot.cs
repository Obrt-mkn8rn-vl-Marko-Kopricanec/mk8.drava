namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamForwardingMetricsSnapshot(long Successes, long Failures, long BodyRelayFailures);
