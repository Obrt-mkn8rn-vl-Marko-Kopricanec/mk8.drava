using Mk8.Drava.Application.BLL.Configuration;

namespace Mk8.Drava.Application.BLL.ControlPlane.RouteDiagnostics;
public static class ProxyRouteDiagnosticsRuntimeConfigurationSnapshotMapper
{
    public static ProxyRouteDiagnosticsRuntimeConfigurationSnapshot FromSources(IEnumerable<RuntimeListener> runtimeListeners, IEnumerable<RuntimeRoute> runtimeRoutes)
    {
        return new ProxyRouteDiagnosticsRuntimeConfigurationSnapshot(runtimeListeners, runtimeRoutes);
    }
}
