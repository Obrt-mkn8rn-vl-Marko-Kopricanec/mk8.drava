namespace Mk8.Drava.Application.BLL.ControlPlane.Routing;
public sealed record ProxyRoutePolicyRedirectInput(bool HttpsRedirectEnabled, int HttpsRedirectStatusCode, int? HttpsRedirectPort, bool CanonicalHostEnabled, string? CanonicalHostTargetHost, int CanonicalHostStatusCode, string ListenerTransport, string RequestHost, string RequestTarget);
