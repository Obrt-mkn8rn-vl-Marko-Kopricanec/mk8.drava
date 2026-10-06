namespace Mk8.Drava.Application.BLL.ControlPlane.RequestDiagnostics;
public sealed class ProxyDiagnosticsAdministrationService
{
    private readonly IProxyRequestDiagnosticsReader _reader;
    public ProxyDiagnosticsAdministrationService(IProxyRequestDiagnosticsReader reader)
    {
        _reader = reader;
    }

    public IReadOnlyList<ProxyRecentRequestDiagnosticEvent> Recent(int limit)
    {
        return _reader.Recent(limit);
    }
}
