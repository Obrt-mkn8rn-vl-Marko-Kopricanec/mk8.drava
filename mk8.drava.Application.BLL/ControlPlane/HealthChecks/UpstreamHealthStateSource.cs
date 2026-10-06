using Mk8.Drava.Application.BLL.Configuration;
using Mk8.Drava.Application.BLL.ControlPlane.Resilience;
using Mk8.Drava.Application.BLL.ControlPlane.Upstreams;

namespace Mk8.Drava.Application.BLL.ControlPlane.HealthChecks;
public sealed record UpstreamHealthStateSource(string UpstreamIdentity, string RouteName, string UpstreamName, string UpstreamEndpoint);
