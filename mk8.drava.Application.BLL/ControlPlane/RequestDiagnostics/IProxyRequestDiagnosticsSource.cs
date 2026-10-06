namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public interface IProxyRequestDiagnosticsSource
{
    IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Recent(int limit);
}
