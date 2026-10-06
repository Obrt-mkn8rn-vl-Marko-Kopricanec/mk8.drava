namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsConfigurationSnapshot
{
    IReadOnlyList<IProxyRouteDiagnosticsListener> Listeners { get; }

    IReadOnlyList<IProxyRouteDiagnosticsRoute> Routes { get; }
}
