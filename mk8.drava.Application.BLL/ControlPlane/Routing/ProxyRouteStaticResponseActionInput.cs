using Mk8.Drava.Application.BLL.Http;
using Mk8.Drava.Application.BLL.ControlPlane.Headers;
using System.Globalization;

namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record ProxyRouteStaticResponseActionInput(int StatusCode, string ContentType, string Body);
