namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsUpstream
{
    string Name { get; }

    string Scheme { get; }

    string Protocol { get; }

    string Endpoint { get; }

    int Weight { get; }

    bool CircuitBreakerEnabled { get; }
}
