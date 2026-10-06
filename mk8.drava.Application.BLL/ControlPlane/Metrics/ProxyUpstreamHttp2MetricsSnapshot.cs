namespace Mk8.Drava.Application.BLL.ControlPlane.Metrics;
public sealed record ProxyUpstreamHttp2MetricsSnapshot(long Requests, long AlpnFailures, long ProtocolErrors);
