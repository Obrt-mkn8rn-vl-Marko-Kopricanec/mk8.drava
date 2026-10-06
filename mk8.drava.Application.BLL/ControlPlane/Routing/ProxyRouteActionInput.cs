using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record ProxyRouteActionInput(ProxyRouteActionKind Action, ProxyRoutePolicyRedirectInput PolicyRedirect, ProxyRouteMaintenanceActionInput Maintenance, ProxyRouteRedirectActionInput Redirect, ProxyRouteStaticResponseActionInput StaticResponse, bool IsUpgradeRequest);
