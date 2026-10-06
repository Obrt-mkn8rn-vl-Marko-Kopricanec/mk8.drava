namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public interface IProxyRouteDiagnosticsListener
{
    string Name { get; }

    string Transport { get; }

    string Address { get; }

    int Port { get; }

    bool Enabled { get; }

    bool SupportsHttp1 { get; }

    bool SupportsHttp2 { get; }

    bool SupportsHttp3 { get; }

    bool Http3EnabledForTraffic { get; }
}
