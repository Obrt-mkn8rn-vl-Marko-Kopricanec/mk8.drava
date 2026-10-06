namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsActionPolicy
{
    ProxyRouteDiagnosticsActionDecision Evaluate(IProxyRouteDiagnosticsRoute route, ProxyRouteDiagnosticsRequestHead requestHead, IProxyRouteDiagnosticsListener listener, bool isUpgradeRequest);
}
