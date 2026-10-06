namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsMetricsSink
{
    void RouteMatchDryRun(string? failureReason);
}
