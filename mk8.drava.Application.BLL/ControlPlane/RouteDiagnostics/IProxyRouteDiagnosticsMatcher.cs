namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsMatcher
{
    IProxyRouteDiagnosticsRoute? Match(IReadOnlyList<IProxyRouteDiagnosticsRoute> routes, ProxyRouteDiagnosticsRequestHead requestHead);
}
