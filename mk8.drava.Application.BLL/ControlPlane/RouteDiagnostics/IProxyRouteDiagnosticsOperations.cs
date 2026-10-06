namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsOperations
{
    RouteMatchDryRunResult Explain(RouteMatchDryRunRequest? request);
}
