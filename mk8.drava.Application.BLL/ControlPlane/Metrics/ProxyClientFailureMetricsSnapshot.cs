namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyClientFailureMetricsSnapshot(long ParseErrors, long BodyRelayFailures, long RequestHeadTimeouts, long RequestBodyTimeouts, long PrematureDisconnects, long DownstreamWriteTimeouts);
