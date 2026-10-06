using Mk8.Drava.Application.BLL.ControlPlane.Caching;
using Mk8.Drava.Application.BLL.ControlPlane.Forwarding;
using Mk8.Drava.Application.BLL.ControlPlane.Routing;

namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public sealed record ProxyRequestRoute(string Name, string SiteName, string Action, bool AccessLogEnabled);
