namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public interface IProxyRequestDiagnosticsReader
{
    IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Recent(int limit);
}
