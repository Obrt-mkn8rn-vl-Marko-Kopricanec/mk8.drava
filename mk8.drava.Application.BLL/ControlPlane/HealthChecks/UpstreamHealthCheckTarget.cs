using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public sealed record UpstreamHealthCheckTarget(string RouteName, string UpstreamName, string UpstreamEndpoint, string UpstreamIdentity, UpstreamTransportEndpoint TransportEndpoint, string Path, TimeSpan Interval, TimeSpan Timeout, int HealthyThreshold, int UnhealthyThreshold);
