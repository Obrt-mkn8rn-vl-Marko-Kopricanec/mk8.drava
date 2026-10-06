namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamFailureMetricsSnapshot(long ConnectFailures, long ConnectTimeouts, long ResponseHeadTimeouts, long ResponseBodyTimeouts, long MalformedResponses, long PrematureDisconnects, long RequestFailures);
